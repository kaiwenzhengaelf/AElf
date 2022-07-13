﻿using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AElf.Modularity;
using AElf.WebApp.Application.Chain;
using AElf.WebApp.Application.Net;
using Google.Protobuf;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Swashbuckle.AspNetCore.SwaggerGen;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Conventions;
using Volo.Abp.Authorization;
using Volo.Abp.Castle;
using Volo.Abp.Castle.DynamicProxy;
using Volo.Abp.Modularity;

namespace AElf.WebApp.Web;

[DependsOn(
    typeof(ChainApplicationWebAppAElfModule),
    typeof(NetApplicationWebAppAElfModule),
    typeof(AbpCastleCoreModule),
    typeof(WebAppAbpAspNetCoreMvcModule))]
public class WebWebAppAElfModule : AElfModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        context.Services.Replace(
            ServiceDescriptor.Transient<IConventionalRouteBuilder, AElfConventionalRouteBuilder>());

        context.Services.AddTransient(typeof(AbpAsyncDeterminationInterceptor<AuthorizationInterceptor>));

        ConfigureAutoApiControllers();

        context.Services.AddApiVersioning(options =>
        {
            options.ApiVersionSelector = new CurrentImplementationApiVersionSelector(options);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ApiVersionReader = new MediaTypeApiVersionReader();
            options.UseApiBehavior = false;
        });
        context.Services.AddVersionedApiExplorer();

        ConfigureSwaggerServices(context.Services);

        context.Services.AddControllers(configure =>
        {
            configure.InputFormatters.Add(new ProtobufInputFormatter());
            configure.OutputFormatters.Add(new ProtobufOutputFormatter());
        }).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = new UpperCamelCaseNamingPolicy();
        });

        context.Services.AddAuthentication("BasicAuthentication")
            .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuthentication", null);

        Configure<BasicAuthOptions>(options => { configuration.GetSection("BasicAuth").Bind(options); });
    }

    private void ConfigureAutoApiControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(ChainApplicationWebAppAElfModule).Assembly,
                setting => { setting.UrlControllerNameNormalizer = _ => "blockChain"; });

            options.ConventionalControllers.Create(typeof(NetApplicationWebAppAElfModule).Assembly,
                setting => { setting.UrlControllerNameNormalizer = _ => "net"; });
        });
    }

    private void ConfigureSwaggerServices(IServiceCollection services)
    {
        services.AddAbpSwaggerGen(
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "AElf API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
                options.DocumentFilter<ApiOptionFilter>();
                options.HideAbpEndpoints();
            }
        );
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            var provider = context.ServiceProvider.GetRequiredService<IApiVersionDescriptionProvider>();
            foreach (var description in provider.ApiVersionDescriptions)
                options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json",
                    $"AELF API {description.GroupName.ToUpperInvariant()}");
        });

        app.UseConfiguredEndpoints();
    }
}

// Thanks to https://tero.teelahti.fi/using-google-proto3-with-aspnet-mvc/
// The input formatter reading request body and mapping it to given data object.
public class ProtobufInputFormatter : InputFormatter
{
    private static readonly MediaTypeHeaderValue ProtoMediaType =
        MediaTypeHeaderValue.Parse((StringSegment)"application/x-protobuf");

    public ProtobufInputFormatter()
    {
        SupportedMediaTypes.Add(ProtoMediaType);
    }

    public override bool CanRead(InputFormatterContext context)
    {
        var request = context.HttpContext.Request;
        MediaTypeHeaderValue.TryParse(request.ContentType, out var requestContentType);

        return requestContentType != null && requestContentType.IsSubsetOf(ProtoMediaType);
    }

    public override Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        try
        {
            var request = context.HttpContext.Request;
            var obj = (IMessage)Activator.CreateInstance(context.ModelType);
            obj.MergeFrom(request.Body);

            return InputFormatterResult.SuccessAsync(obj);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex);
            return InputFormatterResult.FailureAsync();
        }
    }
}

// The output object mapping returned object to Protobuf-serialized response body.
public class ProtobufOutputFormatter : OutputFormatter
{
    private static readonly MediaTypeHeaderValue ProtoMediaType =
        MediaTypeHeaderValue.Parse((StringSegment)"application/x-protobuf");

    public ProtobufOutputFormatter()
    {
        SupportedMediaTypes.Add(ProtoMediaType);
    }

    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        MediaTypeHeaderValue.TryParse(context.ContentType, out var parsedContentType);

        if (context.Object == null || parsedContentType == null ||
            !parsedContentType.IsSubsetOf(ProtoMediaType)) return false;

        // Check whether the given object is a proto-generated object
        return context.ObjectType.GetTypeInfo()
            .ImplementedInterfaces
            .Where(i => i.GetTypeInfo().IsGenericType)
            .Any(i => i.GetGenericTypeDefinition() == typeof(IMessage<>));
    }

    public override Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
    {
        var response = context.HttpContext.Response;

        // Proto-encode
        var protoObj = context.Object as IMessage;
        var serialized = protoObj.ToByteArray();

        return response.Body.WriteAsync(serialized, 0, serialized.Length);
    }
}

//Thanks to https://medium.com/google-cloud/making-newtonsoft-json-and-protocol-buffers-play-nicely-together-fe92079cc91c
/// <summary>
///     Lets Newtonsoft.Json and Protobuf's json converters play nicely
///     together.  The default Netwtonsoft.Json Deserialize method will
///     not correctly deserialize proto messages.
/// </summary>
public class ProtoMessageConverter : JsonConverter
{
    /// <summary>
    ///     Called by NewtonSoft.Json's method to ask if this object can serialize
    ///     an object of a given type.
    /// </summary>
    /// <returns>True if the objectType is a Protocol Message.</returns>
    public override bool CanConvert(Type objectType)
    {
        return typeof(IMessage)
            .IsAssignableFrom(objectType);
    }

    /// <summary>
    ///     Reads the json representation of a Protocol Message and reconstructs
    ///     the Protocol Message.
    /// </summary>
    /// <param name="objectType">The Protocol Message type.</param>
    /// <returns>An instance of objectType.</returns>
    public override object ReadJson(JsonReader reader,
        Type objectType, object existingValue,
        JsonSerializer serializer)
    {
        // The only way to find where this json object begins and ends is by
        // reading it in as a generic ExpandoObject.
        // Read an entire object from the reader.
        var converter = new ExpandoObjectConverter();
        var o = converter.ReadJson(reader, objectType, existingValue,
            serializer);
        // Convert it back to json text.
        var text = JsonConvert.SerializeObject(o);
        // And let protobuf's parser parse the text.
        var message = (IMessage)Activator
            .CreateInstance(objectType);
        return JsonParser.Default.Parse(text,
            message.Descriptor);
    }

    /// <summary>
    ///     Writes the json representation of a Protocol Message.
    /// </summary>
    public override void WriteJson(JsonWriter writer, object value,
        JsonSerializer serializer)
    {
        // Let Protobuf's JsonFormatter do all the work.
        writer.WriteRawValue(JsonFormatter.Default
            .Format((IMessage)value));
    }
}