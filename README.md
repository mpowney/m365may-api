# M365 May API

This project is an Azure Functions runtime API used for the [M365 May online event](https://www.m365may.com).  It enables access to data stored in an Azure Table Storage instance for event attendees, and event co-ordinators.

## Pre-requisites

This is one of three repo's used to deliver the M365 May session handling. The other two repo's are:
* [M365 May Client](https://github.com/mpowney/m365may-client) client side code for attendees to browse sessions via this API
* [M365 May Admin](https://github.com/mpowney/m365may-admin) client side code to allow event co-ordinators to maintain sessions via this API

## Development environment

This app can be developed in any environment supported by dotnet core function apps runtime.  The development environment used by the author was:

* [macOS Mojave](https://support.apple.com/en-au/HT210190) 10.14.6
* [Visual Studio Code](https://code.visualstudio.com) 1.45.1
* [Azure Functions for Visual Studio Code (preview)](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-azurefunctions) v0.22.1


## Configuration

The following environment variables need to be present during runtime to point the solution to supporting resources:

| Variable name | Example | Description |
|---------------|---------|-------------|
| AzureWebJobsStorage | ```DefaultEndpointsProtocol=https;AccountName=functionappstoragename;AccountKey=privatekeyremovedfordiscretion==;BlobEndpoint=https://functionappstoragename.blob.core.windows.net/;QueueEndpoint=https://functionappstoragename.queue.core.windows.net/;TableEndpoint=https://functionappstoragename.table.core.windows.net/;FileEndpoint=https://functionappstoragename.file.core.windows.net/;``` | Connection string pointing to the Function App's storage account |
| AzureStaticSiteStorage | ```DefaultEndpointsProtocol=https;AccountName=staticsitestoragename;AccountKey=privatekeyremovedfordiscretion==;BlobEndpoint=https://staticsitestoragename.blob.core.windows.net/;QueueEndpoint=https://staticsitestoragename.queue.core.windows.net/;TableEndpoint=https://staticsitestoragename.table.core.windows.net/;FileEndpoint=https://staticsitestoragename.file.core.windows.net/;``` | Connection string pointing to the storage account hosting the m365may-client static site |
| SESSIONIZE_SESSIONS_URL | https://sessionize.com/api/v2/kr0di5es/view/Sessions | Use Sessionize to enable an API endpoint, enter the Sessionize API base url here |
| SESSIONIZE_SPEAKERS_URL | https://sessionize.com/api/v2/kr0di5es/view/Speakers | Use Sessionize to enable an API endpoint, enter the Sessionize API base url here |
| SESSIONIZE_CACHE_MINUTES | 1440 | Number of minutes the data obtained from Sessionize is cached in the Function's storage account for |
| REDIRECT_DESTINATION_HOST | https://www.m365may.com | When a session's live event URL is specified as a relative path, this host name will be pre-pended |
| HOLDPAGE_SESSION | https://www.m365may.com/session-details/{id}/ | When a session hasn't yet commenced, they will be redirected to this page instead of the live event URL |
| FREEGEOIP_HOST | https://freegeoip.app | The [Free Geo IP](https://freegeoip.app/) base URL to use for looking up users' IP addresses for geo location information |
| ICAL_FORMAT_TITLE | ```Microsoft 365 May: {title}``` | When generating an calendar event for a session in iCal format, include this text in the title / subject line |
| ICAL_FORMAT_DESCRIPTION | ```{speakers}\r\n\r\n{title}\r\n\r\n{description}\r\n\r\n{url}``` | When generating an calendar event for a session in iCal format, include this text in the description |
| ICAL_FORMAT_UID | ```{id}@m365may.com``` | Use this as a unique identifier so updates to the session schedule will overwrite existing events in the attendee's calendar if they click on the iCal link again |
| FILE_EXPORT_SCHEDULE | ```0 50 * * * *``` | An NCRONTAB expression specifying how often Session, Speaker, and Video Link data files should be exported to the static site (example specifies once per hour at 50 minutes past the hour) |
| FILE_EXPORT_CACHE_CONTROL |  | Influences how the exported Session, Speaker, and Video Link data files are cached by the browser |
| NODE_SYNC_MASTER_CONN | DefaultEndpointsProtocol=https;AccountName=functionappstoragename;AccountKey=privatekeyremovedfordiscretion==;BlobEndpoint=https://functionappstoragename.blob.core.windows.net/;QueueEndpoint=https://functionappstoragename.queue.core.windows.net/;TableEndpoint=https://functionappstoragename.table.core.windows.net/;FileEndpoint=https://functionappstoragename.file.core.windows.net/; | For replica instances of the function app, this connection string is used to connect to the master function app's storage container, so all click counts and geo information are tracked centrally |

## Development

A local instance of this function app can be run with [Visual Studio Code and the Azure Function app extensions](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-first-function-vs-code?pivots=programming-language-csharp).

## Deployment

The built code can be deployed to an [Azure Functions v3 environment](https://azure.microsoft.com/en-us/services/functions/).

This [Azure DevOps build YAML file](./pipeline/build.yml) performs the [pipeline steps in Azure DevOps](https://docs.microsoft.com/en-us/azure/devops/pipelines/yaml-schema?view=azure-devops&tabs=schema%2Cparameter-schema) to create build artefacts.  Build artefacts can then be deployed by an Azure DevOps Release pipeline with the [Deploy Azure Function App action](https://docs.microsoft.com/en-us/azure/devops/pipelines/tasks/deploy/azure-function-app?view=azure-devops).


## Support

This repo can be used as a reference implementation.  It currently exists as a stand-alone solution for M365 May's needs.  Please contact the author to discuss ways to implement a solution for needs described in [this YouTube video](https://www.youtube.com/watch?v=2IvSCB7xk84&list=PL7_cIERhEJUyVmVRNia1VZXMD5zFGJCuT).

