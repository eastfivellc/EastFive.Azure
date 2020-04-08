using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EastFive.Azure.Auth.CredentialProviders
{
    public class IntegrationNameAttribute : Attribute
    {
        public string Name { get; set; }

        public IntegrationNameAttribute(string name)
        {
            this.Name = name;
        }
    }
}
