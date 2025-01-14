﻿using Registry.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Registry.Adapters.Ddb.Model;
using Registry.Ports.DroneDB.Models;

namespace Registry.Web.Models.DTO
{
    public class DeltaDto
    {

        public AddActionDto[] Adds { get; set; }

        public CopyActionDto[] Copies { get; set; }

        public RemoveActionDto[] Removes { get; set; }

    }

    public class AddActionDto
    {
        public string Path { get; set; }

        public EntryType Type { get; set; }

        public override string ToString() =>
            $"ADD -> [{(Type == EntryType.Directory ? 'D' : 'F')}] {Path}";


    }

    public class CopyActionDto
    {
        public string Source { get; set; }
        public string Destination { get; set; }
        public override string ToString() => $"CPY -> {Source} TO {Destination}";
    }

    public class RemoveActionDto
    {
        public string Path { get; set; }

        public EntryType Type { get; set; }

        public override string ToString() => $"DEL -> [{(Type == EntryType.Directory ? 'D' : 'F')}] {Path}";


    }
}
