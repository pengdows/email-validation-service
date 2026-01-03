FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EmailValidation.sln ./
COPY EmailValidation.Api/EmailValidation.Api.csproj EmailValidation.Api/
COPY EmailValidation.Core/EmailValidation.Core.csproj EmailValidation.Core/
COPY EmailValidation.Tests/EmailValidation.Tests.csproj EmailValidation.Tests/

RUN dotnet restore

COPY . ./
RUN dotnet publish EmailValidation.Api/EmailValidation.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0.1-azurelinux3.0-distroless AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=10001:0 /app/publish ./

USER 10001
EXPOSE 8080

ENTRYPOINT ["dotnet", "EmailValidation.Api.dll"]
