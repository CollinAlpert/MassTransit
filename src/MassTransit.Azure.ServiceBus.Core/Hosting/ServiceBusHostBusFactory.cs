// Copyright 2007-2016 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.Azure.ServiceBus.Core.Hosting
{
    using System;
    using Contexts;
    using Logging;
    using MassTransit.Hosting;
    using Microsoft.Azure.Amqp;
    using Microsoft.Azure.ServiceBus;
    using Microsoft.Azure.ServiceBus.Primitives;

    public class ServiceBusHostBusFactory :
        IHostBusFactory
    {
        readonly ServiceBusAmqpTransportSettings _ampAmqpTransportSettings;
        readonly ILog _log = Logger.Get<ServiceBusHostBusFactory>();

        readonly ServiceBusSettings _settings;

        public ServiceBusHostBusFactory(ISettingsProvider settingsProvider)
        {
            ServiceBusSettings settings;
            if (!settingsProvider.TryGetSettings("ServiceBus", out settings))
                throw new ConfigurationException("The ServiceBus settings were not available");

            _settings = settings;

            ServiceBusAmqpTransportSettings amqpTransportSettings;
            if (!settingsProvider.TryGetSettings("ServiceBusAmqpTransport", out amqpTransportSettings))
                throw new ConfigurationException("The ServiceBusAmqpTransport settings were not available");
            _ampAmqpTransportSettings = amqpTransportSettings;
        }

        public IBusControl CreateBus(IBusServiceConfigurator busServiceConfigurator, string serviceName)
        {
            serviceName = serviceName.ToLowerInvariant().Trim().Replace(" ", "_");

            var hostSettings = new SettingsAdapter(_settings, _ampAmqpTransportSettings, serviceName);

            if (hostSettings.ServiceUri == null)
                throw new ConfigurationException("The ServiceBus ServiceUri setting has not been configured");

            return AzureBusFactory.CreateUsingServiceBus(configurator =>
            {
                var host = configurator.Host(hostSettings.ServiceUri, h =>
                {
                    if (!string.IsNullOrWhiteSpace(hostSettings.ConnectionString))
                    {
                        h.TokenProvider = hostSettings.TokenProvider;
                    }
                    else
                    {
                        h.SharedAccessSignature(s =>
                        {
                            s.KeyName = hostSettings.KeyName;
                            s.SharedAccessKey = hostSettings.SharedAccessKey;
                            s.TokenTimeToLive = hostSettings.TokenTimeToLive;
                            s.TokenScope = hostSettings.TokenScope;
                        });
                    }
                });

                if (_log.IsInfoEnabled)
                    _log.Info($"Configuring Host: {hostSettings.ServiceUri}");

                var serviceConfigurator = new ServiceBusServiceConfigurator(configurator, host);

                busServiceConfigurator.Configure(serviceConfigurator);
            });
        }


        class SettingsAdapter :
            ServiceBusHostSettings
        {
            private readonly ServiceBusAmqpTransportSettings _ampAmqpTransportSettings;

            private readonly ServiceBusSettings _settings;

            public SettingsAdapter(ServiceBusSettings settings, 
                ServiceBusAmqpTransportSettings ampAmqpTransportSettings,
                string serviceName)
            {
                _settings = settings;
                _ampAmqpTransportSettings = ampAmqpTransportSettings;

                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                {
                    if (string.IsNullOrWhiteSpace(_settings.Namespace))
                        throw new ConfigurationException("The ServiceBus Namespace setting has not been configured");
                    if (string.IsNullOrEmpty(settings.KeyName))
                        throw new ConfigurationException("The ServiceBus KeyName setting has not been configured");
                    if (string.IsNullOrEmpty(settings.SharedAccessKey))
                        throw new ConfigurationException("The ServiceBus SharedAccessKey setting has not been configured");

                    ServiceUri = AzureServiceBusEndpointUriCreator.Create(_settings.Namespace, _settings.ServicePath ?? serviceName);
                    TokenProvider = Microsoft.Azure.ServiceBus.Primitives.TokenProvider.CreateSharedAccessSignatureTokenProvider(settings.KeyName, settings.SharedAccessKey);
                }
                else
                {
                    var namespaceManager = NamespaceManager.CreateFromConnectionString(settings.ConnectionString);

                    ServiceUri = namespaceManager.Address;
                    TokenProvider = namespaceManager.Settings.TokenProvider;
                }
            }

            public string ConnectionString => _settings.ConnectionString;

            public string KeyName => _settings.KeyName;
            public string SharedAccessKey => _settings.SharedAccessKey;
            public TimeSpan TokenTimeToLive => _settings.TokenTimeToLive ?? TimeSpan.FromDays(1);
            public TokenScope TokenScope => _settings.TokenScope;
            public TimeSpan OperationTimeout => _settings.OperationTimeout ?? TimeSpan.FromSeconds(30);

            public TimeSpan RetryMinBackoff => _settings.RetryMinBackoff ?? TimeSpan.Zero;

            public TimeSpan RetryMaxBackoff => _settings.RetryMaxBackoff ?? TimeSpan.FromSeconds(2);

            public int RetryLimit => _settings.RetryLimit ?? 10;

            public TransportType TransportType => _settings.TransportType;

            public Uri ServiceUri { get; private set; }

            public ITokenProvider TokenProvider { get; private set; }
        }
    }
}