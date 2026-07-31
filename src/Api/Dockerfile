FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY JiraLite.Api.csproj .
RUN dotnet restore JiraLite.Api.csproj

COPY . .
RUN dotnet publish JiraLite.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "JiraLite.Api.dll"]
