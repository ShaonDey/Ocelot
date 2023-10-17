Security Options
================

Ocelot allows you to manage multiple patterns for allowed/blocked IPs using the `IPAddressRange <https://github.com/jsakamoto/ipaddressrange>`_ package with `MPL-2.0 License <https://github.com/jsakamoto/ipaddressrange/blob/master/LICENSE>`_.

This feature is designed to allow greater IP management in order to include or exclude a wide IP range via CIDR notation or IP range.
The current patterns managed are the following:

* Single IP: :code:`192.168.1.1`
* IP Range: :code:`192.168.1.1-192.168.1.250`
* IP Short Range: :code:`192.168.1.1-250`
* IP Range with subnet: :code:`192.168.1.0/255.255.255.0`
* CIDR: :code:`192.168.1.0/24`
* CIDR for IPv6: :code:`fe80::/10`
* The allowed/blocked lists are evaluated during configuration loading
* The *ExcludeAllowedFromBlocked* property is intended to provide the ability to specify a wide range of blocked IP addresses and allow a subrange of IP addresses.
  Default value: :code:`false`
* The absence of a property in **SecurityOptions** is allowed, it takes the default value.

.. code-block:: json

    {
        "Routes": [
            {
                "DownstreamPathTemplate": "/api/service/{Id}",
                "UpstreamPathTemplate": "/api/internal-service/{Id}/full",
                "UpstreamHttpMethod": [
                    "Get"
                ],
                "DownstreamScheme": "http",
                "DownstreamHostAndPorts": [
                    {
                        "Host": "localhost",
                        "Port": 50110
                    }
                ],
                "SecurityOptions": { 
                    "IPBlockedList": [ "192.168.0.0/23" ], 
                    "IPAllowedList": ["192.168.0.15", "192.168.1.15"], 
                    "ExcludeAllowedFromBlocked": true 
                },
            },
        ],
        "GlobalConfiguration": { }
    }

This feature was requested in the `issue 1400 <https://github.com/ThreeMammals/Ocelot/issues/1400>`_.
