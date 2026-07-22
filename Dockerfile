FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS builder
WORKDIR /app
COPY src/Core/Franchise.Domain/Franchise.Domain.csproj src/Core/Franchise.Domain/
COPY src/Core/Franchise.Application/Franchise.Application.csproj src/Core/Franchise.Application/
COPY src/Infrastructure/Franchise.Infrastructure/Franchise.Infrastructure.csproj src/Infrastructure/Franchise.Infrastructure/
COPY src/Presentation/Franchise.Api/Franchise.Api.csproj src/Presentation/Franchise.Api/
RUN dotnet restore src/Presentation/Franchise.Api/Franchise.Api.csproj
COPY src/ src/
RUN dotnet publish src/Presentation/Franchise.Api/Franchise.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app
COPY --from=builder /out .
ENV ASPNETCORE_URLS=http://+:8089
EXPOSE 8089
USER app
ENTRYPOINT ["dotnet", "Franchise.Api.dll"]
