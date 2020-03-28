using API.Configuration;
using API.Extensions;
using API.Helpers;
using AutoMapper;
using Data;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Models;
using Services.Services;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;

namespace API
{
    public class Startup
    {
        public Config Config { get; }

        public IWebHostEnvironment Environment { get; }


        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Config = configuration.GetSection("App").Get<Config>();
            Config.OriginalConfiguration = configuration;
            Environment = environment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<ApplicationDbContext>(o =>
            {
                o.UseSqlServer(Config.OriginalConfiguration.GetConnectionString("DefaultConnection"),
                    sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.EnableRetryOnFailure(maxRetryCount: 50,
                            maxRetryDelay: TimeSpan.FromSeconds(30),
                            errorNumbersToAdd: null);
                    });
            });

            services.AddMvc(options => { options.EnableEndpointRouting = false; })
                .AddFluentValidation(c => c.RegisterValidatorsFromAssemblyContaining<Startup>());

            services.AddAuthorization();

            services.AddAutoMapper();
            services.AddPolicies();

            services.UseConfigurationValidation();
            services.ConfigureValidatableSetting<Config>(Config.OriginalConfiguration.GetSection("App"));

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap.Clear();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = Config.IdentityServer.IdentityUrl;
                    options.RequireHttpsMetadata = false;
                    options.ApiName = Config.Frontend.ClientId;
                    options.ApiSecret = Config.Frontend.ClientSecret;
                    options.EnableCaching = true;

                });

            services.AddCors();

            services.AddSwaggerGen(o =>
            {
                o.SwaggerDoc("v1", new OpenApiInfo() { Title = "Dex API", Version = "v1" });
                o.IncludeXmlComments($@"{AppDomain.CurrentDomain.BaseDirectory}{typeof(Startup).Namespace}.xml");
                o.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme()
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.OAuth2,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer <token>\"",
                });
                o.AddSecurityRequirement(new OpenApiSecurityRequirement()
                {
                    {
                        new OpenApiSecurityScheme()
                        {
                            Reference = new OpenApiReference()
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] { }
                    }
                });
            });

            // Add application services.
            services.AddSingleton(Config);
            services.AddServicesAndRepositories();
        }

        /// <summary>
        /// Configures the specified application.
        /// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <param name="env">The env.</param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            UpdateDatabase(app);
            SeedDatabase(app);
            //IApplicationLifetime applicationLifetime = app.ApplicationServices.GetRequiredService<IApplicationLifetime>();

            if (env.IsDevelopment())
            {
                //app.UseBrowserLink();
                app.UseDeveloperExceptionPage();
                //app.UseDatabaseErrorPage();
            }
            else if (env.IsProduction())
            {
                app.UseExceptionHandler(new ExceptionHandlerOptions
                {
                    ExceptionHandler = (context) =>
                    {
                        context.Response.ContentType = "text/HTML";
                        context.Response.Redirect("/Error.html");
                        return Task.CompletedTask;
                    }
                });
            }
            else
            {
                app.UseExceptionHandler();
            }


            app.UseCors(c =>
            {
                c.WithOrigins(Config.Frontend.FrontendUrl);
                c.SetIsOriginAllowedToAllowWildcardSubdomains();
                c.AllowAnyHeader();
                c.AllowAnyMethod();
            });

            app.UseSwagger();
            app.UseSwaggerUI(o =>
            {
                o.SwaggerEndpoint("/swagger/v1/swagger.json", "Dex API V1");
                o.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
                o.RoutePrefix = "";
                o.DisplayRequestDuration();
            });

            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            //StudentInfo
            app.UseWhen(context =>
                    (context.Request.Method.Equals(HttpMethods.Post)
                     || context.Request.Method.Equals(HttpMethods.Put)) &&
                    context.User.Identities.Any(i => i.IsAuthenticated),
                appBuilder =>
                {
                    appBuilder.Use(async (context, next) =>
                    {
                        DbContext dbContext = context.RequestServices.GetService<DbContext>();
                        IUserService userService = context.RequestServices.GetService<IUserService>();
                        int studentId = context.User.GetStudentId(context);
                        if (await userService.GetUserAsync(studentId) == null)
                        {
                            userService.Add(new User(studentId));
                            await dbContext.SaveChangesAsync();
                        }

                        await next();
                    });
                });

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        /// <summary>
        /// Initializes the database
        /// </summary>
        /// <param name="app"></param>
        private static void UpdateDatabase(IApplicationBuilder app)
        {
            using (var serviceScope = app.ApplicationServices
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope())
            {
                using (var context = serviceScope.ServiceProvider.GetService<ApplicationDbContext>())
                {
                    context.Database.Migrate();
                }
            }
        }

        /// <summary>
        /// Initializes the database
        /// </summary>
        /// <param name="app"></param>
        private static void SeedDatabase(IApplicationBuilder app)
        {
            using (var serviceScope = app.ApplicationServices
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope())
            {
                var seed = new Seed(
                    serviceScope.ServiceProvider.GetService<IMapper>(),
                    serviceScope.ServiceProvider.GetService<IUserService>(),
                    serviceScope.ServiceProvider.GetService<IProjectService>());
                seed.SeedUsers();
                seed.SeedProjects();
            }
        }

    }
}
