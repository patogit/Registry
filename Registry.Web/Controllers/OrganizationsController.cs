﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.OData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Registry.Web.Data;
using Registry.Web.Data.Models;
using Registry.Web.Models;
using Registry.Web.Models.DTO;
using Registry.Web.Services;

namespace Registry.Web.Controllers
{
    [Authorize]
    [ApiController]
    [Route("ddb")]
    public class OrganizationsController : ControllerBaseEx
    {
        private readonly IOptions<AppSettings> _appSettings;
        private readonly UserManager<User> _usersManager;
        private readonly RegistryContext _context;
        private readonly IUtils _utils;

        public OrganizationsController(
            IOptions<AppSettings> appSettings,
            UserManager<User> usersManager,
            RegistryContext context,
            IUtils utils) : base(usersManager)
        {
            _appSettings = appSettings;
            _usersManager = usersManager;
            _context = context;
            _utils = utils;

            // If no organizations in database, let's create the public one
            if (!_context.Organizations.Any())
            {
                var entity = new Organization
                {
                    Id = "public",
                    Name = "Public",
                    CreationDate = DateTime.Now,
                    Description = "Public organization",
                    IsPublic = true,
                    // I don't think this is a good idea
                    OwnerId = usersManager.Users.First().Id
                };
                var ds = new Dataset
                {
                    Id = "default",
                    Name = "Default",
                    Description = "Default dataset",
                    IsPublic = true,
                    CreationDate = DateTime.Now,
                    LastEdit = DateTime.Now
                };
                entity.Datasets = new List<Dataset> { ds };

                _context.Organizations.Add(entity);
                _context.SaveChanges();
            }

        }

        // GET: ddb/
        [HttpGet(Name = nameof(GetAll))]
        public async Task<IQueryable<OrganizationDto>> GetAll()
        {
            var query = from org in _context.Organizations select org;

            if (!await IsUserAdmin())
            {
                var currentUser = await GetCurrentUser();
                query = query.Where(item => item.Id == currentUser.Id || item.IsPublic);
            }

            return from org in query
                   select new OrganizationDto
                   {
                       CreationDate = org.CreationDate,
                       Description = org.Description,
                       Id = org.Id,
                       Name = org.Name,
                       Owner = org.OwnerId,
                       IsPublic = org.IsPublic
                   };
        }

        // GET: ddb/
        [HttpGet("{id}", Name = nameof(Get))]
        public async Task<IActionResult> Get(string id)
        {
            var query = from org in _context.Organizations
                        where org.Id == id
                        select org;

            if (!await IsUserAdmin())
            {
                var currentUser = await GetCurrentUser();
                query = query.Where(item => item.Id == currentUser.Id || item.IsPublic);
            }

            var res = query.FirstOrDefault();

            if (res == null) return NotFound(new ErrorResponse("Organization not found"));

            return Ok(new OrganizationDto(res));
        }

        // POST: ddb/
        [HttpPost]
        public async Task<ActionResult<OrganizationDto>> Post([FromBody] OrganizationDto organization)
        {

            if (!_utils.IsOrganizationNameValid(organization.Id))
                return BadRequest(new ErrorResponse("Invalid organization id"));

            var existingOrg = _context.Organizations.FirstOrDefault(item => item.Id == organization.Id);

            if (existingOrg != null)
                return Conflict(new ErrorResponse("The organization already exists"));

            var currentUser = await GetCurrentUser();

            if (!await IsUserAdmin())
            {

                // If the owner is specified it should be the current user
                if (organization.Owner != null && organization.Owner != currentUser.Id)
                    return Unauthorized(new ErrorResponse("Cannot create a new organization that belongs to a different user"));

                // The current user is the owner
                organization.Owner = currentUser.Id;

            }
            else
            {
                // If no owner specified, the owner is the current user
                if (organization.Owner == null)
                    organization.Owner = currentUser.Id;
                else
                {
                    // Otherwise check if user exists
                    var user = await _usersManager.FindByIdAsync(organization.Owner);

                    if (user == null)
                        return BadRequest(new ErrorResponse($"Cannot find user with id '{organization.Owner}'"));

                }
            }

            var org = organization.ToEntity();
            org.CreationDate = DateTime.Now;

            await _context.Organizations.AddAsync(org);
            await _context.SaveChangesAsync();

            return CreatedAtRoute(nameof(Get), new { id = org.Id }, org);

        }

        // POST: ddb/
        [HttpPut("{id}")]
        public async Task<ActionResult<OrganizationDto>> Put(string id, [FromBody] OrganizationDto organization)
        {

            if (id != organization.Id)
                return BadRequest(new ErrorResponse("Ids don't match"));
            
            if (!_utils.IsOrganizationNameValid(organization.Id))
                return BadRequest(new ErrorResponse("Invalid organization id"));

            var existingOrg = _context.Organizations.FirstOrDefault(item => item.Id == id);

            if (existingOrg == null)
                return NotFound(new ErrorResponse("Cannot find organization with this id"));

            // TODO: I don't know why sometimes we can't get the current user. It happens when we add a new method and we didn't re-authenticate
            var currentUser = await GetCurrentUser();

            if (!await IsUserAdmin())
            {

                // If the owner is specified it should be the current user
                if (organization.Owner != null && organization.Owner != currentUser.Id)
                    return Unauthorized(new ErrorResponse("Cannot create a new organization that belongs to a different user"));

                // The current user is the owner
                organization.Owner = currentUser.Id;

            }
            else
            {
                // If no owner specified, the owner is the current user
                if (organization.Owner == null)
                    organization.Owner = currentUser.Id;
                else
                {
                    // Otherwise check if user exists
                    var user = await _usersManager.FindByIdAsync(organization.Owner);

                    if (user == null)
                        return BadRequest(new ErrorResponse($"Cannot find user with id '{organization.Owner}'"));

                }
            }

            existingOrg.IsPublic = organization.IsPublic;
            existingOrg.Name = organization.Name;
            existingOrg.Description = organization.Description;

            await _context.SaveChangesAsync();

            return NoContent();

        }


    }
}
