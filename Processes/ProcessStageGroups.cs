using EastFive.Api.Controllers;
using EastFive.Collections.Generic;
using EastFive.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure
{
    public struct ProcessStageGroup
    {
        public Guid processStageId;
        public string title;
        public Guid ownerId;
        public Guid processStageTypeId;
        public KeyValuePair<Guid[], Guid>[] confirmableIds;
        public Guid[] editableIds;
        public Guid[] viewableIds;
    }

    public static class ProcessStagesGroups
    {
        public static Guid group1Id = Guid.Parse("aec4f021-0b55-4d8c-afe9-0b8ebff7657f");
        public static Guid group2Id = Guid.Parse("3bf7e23c-d5c7-4d32-8a78-7d4cea311c19");
        
    }
}
