﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Registry.Common;
using Registry.Ports.DroneDB;
using Registry.Ports.DroneDB.Models;
using Registry.Web.Controllers;
using Registry.Web.Data;
using Registry.Web.Data.Models;
using Registry.Web.Exceptions;
using Registry.Web.Models;
using Registry.Web.Models.Configuration;
using Registry.Web.Models.DTO;
using Registry.Web.Services.Ports;
using Registry.Web.Utilities;

namespace Registry.Web.Services.Adapters
{
    // NOTE: This class is a fundamental piece of the architecture because 
    // it encapsulates all the validation logic of the organizations and datasets
    // The logic is centralized here because it could be subject to change

    public class WebUtils : IUtils
    {
        private readonly IAuthManager _authManager;
        private readonly RegistryContext _context;
        private readonly AppSettings _settings;
        private readonly IHttpContextAccessor _accessor;
        private readonly IDdbManager _ddbManager;

        public WebUtils(IAuthManager authManager,
            RegistryContext context,
            IOptions<AppSettings> settings,
            IHttpContextAccessor accessor, IDdbManager ddbManager)
        {
            _authManager = authManager;
            _context = context;
            _accessor = accessor;
            _ddbManager = ddbManager;
            _settings = settings.Value;
        }


        public async Task<Organization> GetOrganization(string orgSlug, bool safe = false, bool checkOwnership = true)
        {
            if (string.IsNullOrWhiteSpace(orgSlug))
                throw new BadRequestException("Missing organization id");

            if (!orgSlug.IsValidSlug())
                throw new BadRequestException("Invalid organization id");

            var org = _context.Organizations.Include(item => item.Datasets)
                .FirstOrDefault(item => item.Slug == orgSlug);

            if (org == null)
                return safe ? null : throw new NotFoundException("Organization not found");

            if (!org.IsPublic && checkOwnership && !await _authManager.IsUserAdmin())
            {
                var currentUser = await _authManager.GetCurrentUser();

                if (currentUser == null)
                    throw new UnauthorizedException("Invalid user");

                if (org.OwnerId != currentUser.Id && org.OwnerId != null && !org.IsPublic)
                    throw new UnauthorizedException("This organization does not belong to the current user");
            }

            return org;
        }

        public async Task<Dataset> GetDataset(string orgSlug, string dsSlug, bool retNullIfNotFound = false,
            bool checkOwnership = true)
        {
            if (string.IsNullOrWhiteSpace(dsSlug))
                throw new BadRequestException("Missing dataset id");

            if (!dsSlug.IsValidSlug())
                throw new BadRequestException("Invalid dataset id");

            if (string.IsNullOrWhiteSpace(orgSlug))
                throw new BadRequestException("Missing organization id");

            if (!orgSlug.IsValidSlug())
                throw new BadRequestException("Invalid organization id");

            var org = _context.Organizations
                .Include(item => item.Datasets)
                .FirstOrDefault(item => item.Slug == orgSlug);

            if (org == null)
                throw new NotFoundException("Organization not found");

            var dataset = org.Datasets.FirstOrDefault(item => item.Slug == dsSlug);

            if (dataset == null)
            {
                if (retNullIfNotFound) return null;

                throw new NotFoundException("Cannot find dataset");
            }

            var ddb = _ddbManager.Get(orgSlug, dataset.InternalRef);
            var attributes = ddb.GetAttributes();

            if (!attributes.IsPublic && !await _authManager.IsUserAdmin() && checkOwnership)
            {
                var currentUser = await _authManager.GetCurrentUser();

                if (currentUser == null)
                    throw new UnauthorizedException("Invalid user");

                if (org.OwnerId != null && org.OwnerId != currentUser.Id)
                    throw new UnauthorizedException("This organization does not belong to the current user");

            }

            return dataset;
        }

        public string GetFreeOrganizationSlug(string orgName)
        {
            if (string.IsNullOrWhiteSpace(orgName))
                throw new BadRequestException("Empty organization name");

            var slug = orgName.ToSlug();

            var res = slug;

            for (var n = 1; ; n++)
            {
                var org = _context.Organizations.FirstOrDefault(item => item.Slug == res);

                if (org == null) return res;

                res = slug + "-" + n;
            }
        }

        public string GenerateDatasetUrl(Dataset dataset)
        {
            bool isHttps;
            string host;

            if (!string.IsNullOrWhiteSpace(_settings.ExternalUrlOverride))
            {
                var uri = new Uri(_settings.ExternalUrlOverride);

                isHttps = uri.Scheme.ToLowerInvariant() == "https";
                host = uri.Host;

                // Mmmm
                if (uri.Port != 443 && uri.Port != 80)
                    host += ":" + uri.Port;
            }
            else
            {
                var context = _accessor.HttpContext;
                host = context?.Request.Host.ToString() ?? "localhost";
                isHttps = context?.Request.IsHttps ?? false;
            }

            var scheme = isHttps ? "ddb" : "ddb+unsafe";

            var datasetUrl = string.Format($"{scheme}://{host}/{dataset.Organization.Slug}/{dataset.Slug}");

            return datasetUrl;
        }

        private const string BucketNameFormat = "{0}-{1}";

        public string GetBucketName(string orgSlug, Guid internalRef)
        {
            return string.Format(BucketNameFormat, orgSlug, internalRef.ToString()).ToLowerInvariant();
        }

        // NOTE: This function can be optimized down the line.
        // It currently enumerates all the datasets and asks DDB for size.
        // If we notice a slowdown of the upload/share/push process this could be the culprit
        // A simple cache level could be the solution but it needs to be kept in sync
        // with all the dataset operations. Really a pain if it is not necessary.

        public UserStorageInfo GetUserStorage(User user)
        {

            if (user == null)
                throw new ArgumentException("User is null", nameof(user));

            // This is pure C# magics
            var maxStorage = user.Metadata?.SafeGetValue(MagicStrings.MaxStorageKey) is long obj ? obj : (long?)null;

            // Get all the datasets that belong to the user
            var datasets = (from org in _context.Organizations
                where org.OwnerId == user.Id
                from dataset in org.Datasets
                select new { OrgSlug = org.Slug, dataset.InternalRef}).ToArray();

            // Get the size and sum
            var size =
                (from ds in datasets
                 let ddb = _ddbManager.Get(ds.OrgSlug, ds.InternalRef)
                 select ddb.GetSize()).Sum();

            return new UserStorageInfo
            {
                // Max storage is in MB, we need bytes to stay consistent
                Total = maxStorage * 1024 * 1024,
                Used = size
            };

        }

        public async Task CheckCurrentUserStorage(long size = 0)
        {
            if (!_settings.EnableStorageLimiter) return;

            // Admins don't have limits
            if (await _authManager.IsUserAdmin()) return;

            var storageInfo = GetUserStorage(await _authManager.GetCurrentUser());

            if (storageInfo.Total != null && storageInfo.Used + size > storageInfo.Total)
                throw new MaxUserStorageException(storageInfo.Used, storageInfo.Total);

        }
    }
}