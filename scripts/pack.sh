dotnet pack --configuration Release --property:PackageOutputPath="$PWD/nupkgs" -p:ContinuousIntegrationBuild=true 
# push
# https://learn.microsoft.com/zh-cn/nuget/nuget-org/publish-a-package
cd nupkgs
dotnet nuget push "*.nupkg" --api-key ${NUGET_ORG_API_KEY} --source https://api.nuget.org/v3/index.json