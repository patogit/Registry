﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Registry.Web.Models.DTO;

namespace Registry.Web.Services.Ports
{
    public interface ILoginManager
    {
        public Task<LoginResult> CheckAccess(string userName, string password);
        public Task<LoginResult> CheckAccess(string token);
    }
}
