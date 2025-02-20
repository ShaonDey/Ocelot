Middleware Injection and Overrides
==================================

Warning use with caution. If you are seeing any exceptions or strange behavior in your middleware 
pipeline and you are using any of the following. Remove them and try again!

When setting up Ocelot in your Startup.cs you can provide some additional middleware 
and override middleware. This is done as follows.

.. code-block:: csharp

    var configuration = new OcelotPipelineConfiguration
    {
        PreErrorResponderMiddleware = async (ctx, next) =>
        {
            await next.Invoke();
        }
    };

    app.UseOcelot(configuration);

In the example above the provided function will run before the first piece of Ocelot middleware. 
This allows a user to supply any behaviors they want before and after the Ocelot pipeline has run.
This means you can break everything so use at your own pleasure!

The user can set functions against the following.

* PreErrorResponderMiddleware - Already explained above.

* PreAuthenticationMiddleware - This allows the user to run pre authentication logic and then call Ocelot's authentication middleware.

* AuthenticationMiddleware - This overrides Ocelots authentication middleware.

* PreAuthorizationMiddleware - This allows the user to run pre authorization logic and then call Ocelot's authorization middleware.

* AuthorizationMiddleware - This overrides Ocelots authorization middleware.

* PreQueryStringBuilderMiddleware - This allows the user to manipulate the query string on the http request before it is passed to Ocelots request creator.

Obviously you can just add mentioned Ocelot's middleware overridings as normal before the call to ``app.UseOcelot()``.
It cannot be added after as Ocelot does not call the next Ocelot's middleware overridings based on specified middleware configuration.
So, the next called middlewares won't affect Ocelot configuration.

ASP.NET Core Middlewares and Ocelot Pipeline Builder
----------------------------------------------------

Ocelot pipeline is a part of entire `ASP.NET Core Middlewares <https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-7.0>`_ conveyor aka app pipeline.
The `BuildOcelotPipeline <https://github.com/search?q=repo%3AThreeMammals%2FOcelot+BuildOcelotPipeline+path%3A%2F%5Esrc%5C%2FOcelot%5C%2FMiddleware%5C%2F%2F&type=code>`_ method encapsulates Ocelot pipeline.
The last middleware in the **BuildOcelotPipeline** method is **HttpRequesterMiddleware** that calls the next middleware.

The internal `HttpRequesterMiddleware <https://github.com/search?q=repo%3AThreeMammals%2FOcelot+HttpRequesterMiddleware+path%3A%2F%5Esrc%5C%2FOcelot%5C%2F%2F&type=code>`_ is part of the pipeline but it is private and cannot be overridden, since this middleware does not belong to the list of user's one that can be overridden!
So, this is `the last middleware <https://github.com/search?q=repo%3AThreeMammals%2FOcelot+UseHttpRequesterMiddleware&type=code>`_ of the entire Ocelot and ASP.NET pipeline, and it handles non-user operation.
The last user middleware that can be overridden is `PreQueryStringBuilderMiddleware <https://github.com/search?q=repo%3AThreeMammals%2FOcelot+PreQueryStringBuilderMiddleware+language%3AC%23&type=code&l=C%23>`_ being read from the `pipeline configuration object <https://github.com/search?q=repo%3AThreeMammals%2FOcelot%20%22OcelotPipelineConfiguration%20pipelineConfiguration%22&type=code>`_, see previous section.

Considering that **PreQueryStringBuilderMiddleware** and **HttpRequesterMiddleware** are the last user and system middlewares, there are no other middlewares in the pipeline at all.
But you can still extend the ASP.NET pipeline, as shown in the following code:

.. code-block:: csharp

    app.UseOcelot().Wait();
    app.UseMiddleware<MyCustomMiddleware>();

But we do not recommend adding this custom middleware before or after calling of ``UseOcelot()`` as it affects the stability of the entire pipeline and has not been tested.
Such kind of custom pipeline building is out of the Ocelot pipeline model and the quality of the solution is at your own risk.

Finally, don't get confused about the distinction between system (private) and user (public) middlewares.
Private middlewares are hidden and cannot be overridden, but the entire ASP.NET pipeline can still be extended.
The public middlewares are fully customizable and can be overridden.
