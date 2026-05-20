FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/AutoMeatBot.Api/AutoMeatBot.Api.csproj src/AutoMeatBot.Api/
RUN dotnet restore src/AutoMeatBot.Api/AutoMeatBot.Api.csproj

COPY . .
RUN test -f src/AutoMeatBot.Api/Data/AppDbContext.cs
RUN dotnet publish src/AutoMeatBot.Api/AutoMeatBot.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AutoMeatBot.Api.dll"]
