﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Registry.Ports.DroneDB;

namespace Registry.Web.Services.Ports
{
    public interface ICacheManager
    {
        void Register(string seed, Func<object[], byte[]> getData, TimeSpan? expiration = null);
        void Unregister(string seed);
        Task<string> Get(string seed, string category, params object[] parameters);
        Task Clear(string seed, string category = null);
        void Remove(string seed, string category, params object[] parameters);
    }
}
