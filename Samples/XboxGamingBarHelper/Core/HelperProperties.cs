using Shared.Data;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.Foundation.Collections;

namespace XboxGamingBarHelper.Core
{
    internal class HelperProperties : FunctionalProperties
    {
        public HelperProperties(params FunctionalProperty[] inProperties) : base(inProperties) { }

        protected override Task<AppServiceResponseStatus> SendResponse(AppServiceRequest request, ValueSet response)
        {
            return request.SendResponseAsync(response).AsTask();
        }
    }
}
