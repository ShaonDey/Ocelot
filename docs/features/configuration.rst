Configuration
=============

An example configuration can be found `here <https://github.com/ThreeMammals/Ocelot/blob/develop/test/Ocelot.ManualTest/ocelot.json>`_. There are two sections to the configuration. An array of Routes and a GlobalConfiguration. The Routes are the objects that tell Ocelot how to treat an upstream request. The Global configuration is a bit hacky and allows overrides of Route specific settings. It's useful if you don't want to manage lots of Route specific settings.

.. code-block:: json

    {
        "Routes": [],
        "GlobalConfiguration": {}
    }

Here is an example Route configuration, You don't need to set all of these things but this is everything that is available at the moment:

.. code-block:: json

  {
            "DownstreamPathTemplate": "/",
            "UpstreamPathTemplate": "/",
            "UpstreamHttpMethod": [
                "Get"
            ],
            "DownstreamHttpMethod": "",
            "DownstreamHttpVersion": "",
            "AddHeadersToRequest": {},
            "AddClaimsToRequest": {},
            "RouteClaimsRequirement": {},
            "AddQueriesToRequest": {},
            "RequestIdKey": "",
            "FileCacheOptions": {
                "TtlSeconds": 0,
                "Region": ""
            },
            "SecurityOptions": {
                "IPAllowedList": [],
                "IPBlockedList": []
            },
            "RouteIsCaseSensitive": false,
            "ServiceName": "",
            "DownstreamScheme": "http",
            "DownstreamHostAndPorts": [
                {
                    "Host": "localhost",
                    "Port": 51876,
                }
            ],
            "QoSOptions": {
                "ExceptionsAllowedBeforeBreaking": 0,
                "DurationOfBreak": 0,
                "TimeoutValue": 0
            },
            "LoadBalancer": "",
            "RateLimitOptions": {
                "ClientWhitelist": [],
                "EnableRateLimiting": false,
                "Period": "",
                "PeriodTimespan": 0,
                "Limit": 0
            },
            "AuthenticationOptions": {
                "AuthenticationProviderKey": "",
                "AllowedScopes": []
            },
            "HttpHandlerOptions": {
                "AllowAutoRedirect": true,
                "UseCookieContainer": true,
                "UseTracing": true,
                "MaxConnectionsPerServer": 100
            },
            "DangerousAcceptAnyServerCertificateValidator": false,
            "SecurityOptions": {
                "IPAllowedList": [],
                "IPBlockedList": [],
                "ExcludeAllowedFromBlocked": false
            }
        }

More information on how to use these options is below.

Multiple environments
---------------------

Like any other ASP.NET Core project Ocelot supports configuration file names such as **configuration.dev.json**, **configuration.test.json** etc. In order to implement this add the following 
to you:

.. code-block:: csharp

        .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config
                        .SetBasePath(hostingContext.HostingEnvironment.ContentRootPath)
                        .AddJsonFile("appsettings.json", true, true)
                        .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true)
                        .AddJsonFile("ocelot.json")
                        .AddJsonFile($"configuration.{hostingContext.HostingEnvironment.EnvironmentName}.json")
                        .AddEnvironmentVariables();
                })

Ocelot will now use the environment specific configuration and fall back to **ocelot.json** if there isn't one.

You also need to set the corresponding environment variable which is ``ASPNETCORE_ENVIRONMENT``.
More info on this can be found in the ASP.NET Core docs: `Use multiple environments in ASP.NET Core <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments?view=aspnetcore-7.0>`_.

Merging configuration files
---------------------------

This feature was requested in `Issue 296 <https://github.com/ThreeMammals/Ocelot/issues/296>`_ and allows users to have multiple configuration files to make managing large configurations easier.

Instead of adding the configuration directly e.g. ``AddJsonFile("ocelot.json")`` you can call ``AddOcelot()`` like below. 

.. code-block:: csharp

    .ConfigureAppConfiguration((hostingContext, config) =>
        {
            config
                .SetBasePath(hostingContext.HostingEnvironment.ContentRootPath)
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true)
                .AddOcelot(hostingContext.HostingEnvironment)
                .AddEnvironmentVariables();
        })

In this scenario Ocelot will look for any files that match the pattern ``(?i)ocelot.([a-zA-Z0-9]*).json`` and then merge these together. If you want to set the GlobalConfiguration property you must have a file called **ocelot.global.json**. 

The way Ocelot merges the files is basically load them, loop over them, add any Routes, add any AggregateRoutes and if the file is called **ocelot.global.json** add the GlobalConfiguration aswell as any Routes or AggregateRoutes.
Ocelot will then save the merged configuration to a file called ocelot.json and this will be used as the source of truth while ocelot is running.

At the moment there is no validation at this stage it only happens when Ocelot validates the final merged configuration. This is something to be aware of when you are investigating problems. 
I would advise always checking what is in **ocelot.json** file if you have any problems.

You can also give Ocelot a specific path to look in for the configuration files like below.

.. code-block:: csharp

    .ConfigureAppConfiguration((hostingContext, config) =>
        {
            config
                .SetBasePath(hostingContext.HostingEnvironment.ContentRootPath)
                .AddJsonFile("appsettings.json", true, true)
                .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", true, true)
                .AddOcelot("/foo/bar", hostingContext.HostingEnvironment)
                .AddEnvironmentVariables();
        })

Ocelot needs the HostingEnvironment so it knows to exclude anything environment specific from the algorithm. 

Store configuration in Consul
-----------------------------

The first thing you need to do is install the NuGet package that provides Consul support in Ocelot.

.. code-block:: powershell

    Install-Package Ocelot.Provider.Consul

Then you add the following when you register your services Ocelot will attempt to store and retrieve its configuration in Consul KV store.
In order to register Consul services we must call the ``AddConsul()`` and ``AddConfigStoredInConsul()`` extensions using the ``OcelotBuilder`` being returned by ``AddOcelot()`` [#f1]_ like below:

.. code-block:: csharp

 services
    .AddOcelot()
    .AddConsul()
    .AddConfigStoredInConsul();

You also need to add the following to your **ocelot.json**. This is how Ocelot finds your Consul agent and interacts to load and store the configuration from Consul.

.. code-block:: json

    "GlobalConfiguration": {
        "ServiceDiscoveryProvider": {
            "Host": "localhost",
            "Port": 9500
        }
    }

I decided to create this feature after working on the Raft consensus algorithm and finding out its super hard. Why not take advantage of the fact Consul already gives you this! 
I guess it means if you want to use Ocelot to its fullest you take on Consul as a dependency for now.

This feature has a **3** seconds TTL cache before making a new request to your local Consul agent.

Reload JSON config on change
----------------------------

Ocelot supports reloading the json configuration file on change. For instance, the following will recreate Ocelot's internal configuration when the **ocelot.json** file is updated manually:

.. code-block:: csharp

    config.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

Configuration Key
-----------------

If you are using Consul for configuration (or other providers in the future) you might want to key your configurations so you can have multiple configurations :) 
This feature was requested in `Issue 346 <https://github.com/ThreeMammals/Ocelot/issues/346>`_! In order to specify the key you need to set the ConfigurationKey property in the ServiceDiscoveryProvider section of the configuration json file e.g.

.. code-block:: json

    "GlobalConfiguration": {
        "ServiceDiscoveryProvider": {
            "Host": "localhost",
            "Port": 9500,
            "ConfigurationKey": "Oceolot_A"
        }
    }

In this example Ocelot will use "Oceolot_A" as the key for your configuration when looking it up in Consul.

If you do not set the ConfigurationKey Ocelot will use the string InternalConfiguration as the key.

Follow Redirects / Use CookieContainer 
--------------------------------------

Use HttpHandlerOptions in Route configuration to set up HttpHandler behavior:

1. AllowAutoRedirect is a value that indicates whether the request should follow redirection responses. Set it true if the request should automatically follow redirection responses from the Downstream resource; otherwise false. The default value is ``false``.

2. UseCookieContainer is a value that indicates whether the handler uses the CookieContainer property to store server cookies and uses these cookies when sending requests. The default value is ``false``. 
Please note that if you are using the CookieContainer Ocelot caches the ``HttpClient`` for each downstream service. This means that all requests to that DownstreamService will share the same cookies. 
`Issue 274 <https://github.com/ThreeMammals/Ocelot/issues/274>`_ was created because a user noticed that the cookies were being shared. I tried to think of a nice way to handle this but I think it is impossible. 
If you don't cache the clients that means each request gets a new client and therefore a new cookie container. If you clear the cookies from the cached client container you get race conditions due to inflight requests. 
This would also mean that subsequent requests don't use the cookies from the previous response! All in all not a great situation. I would avoid setting UseCookieContainer to ``true`` unless you have a really really good reason. Just look at your response headers and forward the cookies back with your next request! 

MaxConnectionsPerServer property
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

This controls how many connections the internal HttpClient will open. This can be set at Route or global level.

SSL Errors
----------

If you want to ignore SSL warnings / errors set the following in your Route config.

.. code-block:: json

    "DangerousAcceptAnyServerCertificateValidator": true

I don't recommend doing this, I suggest creating your own certificate and then getting it trusted by your local / remote machine if you can.

React to Configuration Changes
------------------------------

Resolve ``IOcelotConfigurationChangeTokenSource`` from the DI container if you wish to react to changes to the Ocelot configuration via the Ocelot.Administration API 
or **ocelot.json** being reloaded from the disk. You may either poll the change token's HasChanged property, or register a callback with the ``RegisterChangeCallback`` method.

Polling the HasChanged property
^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^

.. code-block:: csharp

    public class ConfigurationNotifyingService : BackgroundService
    {
        private readonly IOcelotConfigurationChangeTokenSource _tokenSource;
        private readonly ILogger _logger;
        public ConfigurationNotifyingService(IOcelotConfigurationChangeTokenSource tokenSource, ILogger logger)
        {
            _tokenSource = tokenSource;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_tokenSource.ChangeToken.HasChanged)
                {
                    _logger.LogInformation("Configuration updated");
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
    
Registering a callback
^^^^^^^^^^^^^^^^^^^^^^

.. code-block:: csharp

    public class MyDependencyInjectedClass : IDisposable
    {
        private readonly IOcelotConfigurationChangeTokenSource _tokenSource;
        private readonly IDisposable _callbackHolder;
        public MyClass(IOcelotConfigurationChangeTokenSource tokenSource)
        {
            _tokenSource    = tokenSource;
            _callbackHolder = tokenSource.ChangeToken.RegisterChangeCallback(_ => Console.WriteLine("Configuration changed"), null);
        }
        public void Dispose()
        {
            _callbackHolder.Dispose();
        }
    }

DownstreamHttpVersion
---------------------

Ocelot allows you to choose the HTTP version it will use to make the proxy request. It can be set as "1.0", "1.1" or "2.0".

""""

.. [#f1] The ``AddOcelot`` method adds default ASP.NET services to DI-container. You could call another more extended ``AddOcelotUsingBuilder`` method while configuring services to build and use custom builder via an ``IMvcCoreBuilder`` interface object. See more instructions in :doc:`../features/dependencyinjection`, "**The AddOcelotUsingBuilder method**" section.
