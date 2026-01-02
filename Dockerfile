FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EmailValidation.sln ./
COPY EmailValidation.Api/EmailValidation.Api.csproj EmailValidation.Api/
COPY EmailValidation.Core/EmailValidation.Core.csproj EmailValidation.Core/
COPY EmailValidation.Tests/EmailValidation.Tests.csproj EmailValidation.Tests/

RUN dotnet restore

COPY . ./
RUN dotnet publish EmailValidation.Api/EmailValidation.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

RUN apt-get update \
    && apt-get install -y --no-install-recommends dnsutils \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
RUN chown -R 10001:0 /app

USER 10001
EXPOSE 8080

ENTRYPOINT ["dotnet", "EmailValidation.Api.dll"]
