@echo off
cls

dotnet build LINGYUN.MicroService.All.sln
dotnet build LINGYUN.MicroService.Common.sln
dotnet build LINGYUN.MicroService.TaskManagement.sln
dotnet build LINGYUN.MicroService.WebhooksManagement.sln
dotnet build LINGYUN.MicroService.Workflow.sln
pause

