﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Registry.Web.Models;
using Registry.Web.Models.DTO;

namespace Registry.Web.Services.Ports
{
    public interface IObjectsManager
    {
        Task<IEnumerable<EntryGeoDto>> List(string orgSlug, string dsSlug, string path = null, bool recursive = false);

        Task<IEnumerable<EntryGeoDto>> Search(string orgSlug, string dsSlug, string query = null, string path = null,
            bool recursive = true);

        Task<StorageEntryDto> Get(string orgSlug, string dsSlug, string path);
        Task<EntryGeoDto> AddNew(string orgSlug, string dsSlug, string path, byte[] data);
        Task<EntryGeoDto> AddNew(string orgSlug, string dsSlug, string path, Stream stream = null);
        Task Move(string orgSlug, string dsSlug, string source, string dest);
        Task Delete(string orgSlug, string dsSlug, string path);
        Task DeleteAll(string orgSlug, string dsSlug);
        Task<FileStreamDescriptor> DownloadStream(string orgSlug, string dsSlug, string[] paths);
        Task<StorageFileDto> GenerateThumbnail(string orgSlug, string dsSlug, string path, int? size, bool recreate = false);
        Task<StorageFileDto> GenerateTile(string orgSlug, string dsSlug, string path, int tz, int tx, int ty, bool retina);
        Task<FileStreamDescriptor> GetDdb(string orgSlug, string dsSlug);
        Task Build(string orgSlug, string dsSlug, string path, bool background = false, bool force = false);
        Task<string> GetBuildFile(string orgSlug, string dsSlug, string hash, string path);
        Task<bool> CheckBuildFile(string orgSlug, string dsSlug, string hash, string path);
    }
}
