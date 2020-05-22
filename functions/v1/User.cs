using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using System.Collections.Generic;
using com.m365may.entities;
using com.m365may.utils;

namespace com.m365may.v1
{

    public static partial class Permissions {
        public const string UserAdministrator = "/User/Administrator";
    }

    public static class User
    {

        [FunctionName("UsersGet")]
        public static async Task<IActionResult> UsersGet (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_api/v1/users")] HttpRequest req,
            [Table(TableNames.Users)] CloudTable userTable,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            UserEntity currentUser = await UserEntity.get(userTable, claimsPrincipal.Identity.Name);
            if (!currentUser.HasPermission(Permissions.UserAdministrator)) {
                return new UnauthorizedResult();
            }

            List<UserEntity> entities = await UserEntity.get(userTable);
            if (entities == null) {
                return new NotFoundResult();
            }

            return new OkObjectResult(entities.ToArray());

        }

        [FunctionName("UserGet")]
        public static async Task<IActionResult> UserGet (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_api/v1/users/{key}")] HttpRequest req,
            [Table(TableNames.Users)] CloudTable userTable,
            string key,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            UserEntity currentUser = await UserEntity.get(userTable, claimsPrincipal.Identity.Name);
            if (!currentUser.HasPermission(Permissions.UserAdministrator)) {
                if (key != claimsPrincipal.Identity.Name) {
                    return new UnauthorizedResult();
                }
            }

            UserEntity entity = await UserEntity.get(userTable, key);
            if (entity == null) {
                return new NotFoundResult();
            }

            return new OkObjectResult(entity);

        }

        [FunctionName("UserMeGet")]
        public static async Task<IActionResult> UserMeGet (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_api/v1/user/me")] HttpRequest req,
            [Table(TableNames.Users)] CloudTable userTable,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            UserEntity entity = await UserEntity.get(userTable, claimsPrincipal.Identity.Name);
            if (entity == null || entity.RowKey == "invalid") {
                return new NotFoundResult();
            }

            return new OkObjectResult(entity);

        }
        
        [FunctionName("UserDelete")]
        public static async Task<IActionResult> UserDelete (
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "_api/v1/users/{key}")] HttpRequest req,
            [Table(TableNames.Users)] CloudTable userTable,
            string key,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            UserEntity currentUser = await UserEntity.get(userTable, claimsPrincipal.Identity.Name);
            if (!currentUser.HasPermission(Permissions.UserAdministrator)) {
                return new UnauthorizedResult();
            }

            UserEntity entity = await UserEntity.get(userTable, key);
            if (entity == null) {
                return new NotFoundResult();
            }

            bool deleteSuccess = await UserEntity.delete(userTable, entity);
            return deleteSuccess ? (IActionResult)new OkObjectResult(entity) : new BadRequestResult();

        }

        [FunctionName("UserPost")]
        public static async Task<IActionResult> UserPost (
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "_api/v1/users")] HttpRequest req,
            [Table(TableNames.Users)] CloudTable userTable,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            UserEntity currentUser = await UserEntity.get(userTable, claimsPrincipal.Identity.Name);
            if (!currentUser.HasPermission(Permissions.UserAdministrator)) {
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            UserEntity entity = JsonConvert.DeserializeObject<UserEntity>(requestBody);

            if (entity.RowKey == null || entity.Permissions == null) {
                return new BadRequestObjectResult($"Please specify the key and permissions parameters in the request body");
            }

            log.LogInformation($"Getting Redirect row for values {entity.RowKey}");
            UserEntity existingEntity = await UserEntity.get(userTable, entity.RowKey);
            if (existingEntity != null) {
                return new BadRequestObjectResult($"Redirect with {entity.RowKey} already exists");
            }

            bool success = await UserEntity.put(userTable, entity.RowKey, entity.Permissions);
            if (!success) {
                return new BadRequestObjectResult($"Error occurred creating {entity.RowKey} already exists");
            }

            return new OkResult();
            
        }

        [FunctionName("UserPatch")]
        public static async Task<IActionResult> UserPatch (
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "_api/v1/users/{key}")] HttpRequest req,
            [Table(TableNames.Users)] CloudTable userTable,
            string key,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            UserEntity currentUser = await UserEntity.get(userTable, claimsPrincipal.Identity.Name);
            if (!currentUser.HasPermission(Permissions.UserAdministrator)) {
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic entity = JsonConvert.DeserializeObject<dynamic>(requestBody);

            log.LogInformation($"Getting Redirect row for values {claimsPrincipal.Identity.Name} and {entity.RowKey}");
            UserEntity existingEntity = await UserEntity.get(userTable, key);
            if (existingEntity == null) {
                return new BadRequestObjectResult($"Redirect with {key} doesn't exist for {claimsPrincipal.Identity.Name}");
            }

            existingEntity.Permissions = entity.Permissions ??= existingEntity.Permissions;

            bool success = await UserEntity.put(userTable, existingEntity);
            if (!success) {
                return new BadRequestObjectResult($"Error occurred updating {key} for {claimsPrincipal.Identity.Name}");
            }

            return new OkResult();
            
        }

    }

}

