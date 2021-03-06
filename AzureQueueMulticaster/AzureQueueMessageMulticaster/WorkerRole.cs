using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;

using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;

using Aspectacular;

namespace AzureQueueMessageMulticaster
{
    public class WorkerRole : RoleEntryPoint
    {
        private AzureQueueMulticastRouteConfiguration routes;

        #region Worker Role Overrides

        static WorkerRole()
        {
            Aspect.DefaultAspectFactory = () =>
                            new Aspect[] 
                            {
                                new TraceOutputAspect(),
                                new StopwatchAspect(detailed: false), 

#if DEBUG
                                new SlowFullMethodSignatureAspect(), 
                                new ReturnValueLoggerAspect(),
#endif
                            };
        }

        public override bool OnStart()
        {
            InitializeEnvironment();

            // Load queue route info from 
            this.routes = AOP.Invoke(() => AzureQueueMulticastRouteConfiguration.LoadFromAzureRoleSettings("AzureQueueMulticastRoutes"));
            this.routes.GetProxy().Invoke(theRoutes => theRoutes.BeginAsyncMessageForwarding());

            return base.OnStart();
        }

        private static void InitializeEnvironment()
        {
            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            WriteRoleInstanceInfo();

            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = Environment.ProcessorCount * 12;

            //RoleEnvironment.Changing += this.RoleEnvironmentChanging;
        }

        public override void OnStop()
        {
            this.routes.EndMessageForwarding();

            base.OnStop();
        }

        //private void RoleEnvironmentChanging(object sender, RoleEnvironmentChangingEventArgs e)
        //{
        //    // Add code for handling changes
        //}

        #endregion Worker Role Overrides

        #region Utility Methods

        //private static AzureQueueMulticastRouteConfiguration CreateFakeConfiguration()
        //{
        //    var routes = new AzureQueueMulticastRouteConfiguration();

        //    var route = new AzureQueueMulticastRoute();
        //    route.SourceQueue = new AzureSourceQueueConnection
        //    {
        //        ConnectionStringName = "QueueStorageAccountConnectionString",
        //        QueueName = "dummysourcequeue",
        //        MaxDelayBetweenDequeueAttemptsSeconds = 15,
        //        MessageInivisibilityTimeMillisec = 10 * 1000,
        //    };
        //    route.DestinationQueues.Add(new AzureDestinationQueueConnection { ConnectionStringName = "QueueStorageAccountConnectionString", QueueName = "destionationuno" });
        //    route.DestinationQueues.Add(new AzureDestinationQueueConnection { ConnectionStringName = "QueueStorageAccountConnectionString", QueueName = "destionationdos" });

        //    routes.Add(route);

        //    string xmlConfig = routes.ToXml(formatForRoleSettings: false);
        //    Trace.TraceInformation("Multicast route configuration:\r\n{0}", xmlConfig);

        //    return routes;
        //}

        public static void WriteRoleInstanceInfo()
        {
            foreach (RoleInstance roleInst in RoleEnvironment.CurrentRoleInstance.Role.Instances)
            {
                Trace.TraceInformation("Instance ID: {0}", roleInst.Id);
                foreach (RoleInstanceEndpoint roleInstEndpoint in roleInst.InstanceEndpoints.Values)
                {
                    Trace.TraceInformation("Instance endpoint IP address and port: {0}", roleInstEndpoint.IPEndpoint);
                }
            }
        }

        #endregion Utility Methods
    }
}
