FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CoffeeShop.Web/CoffeeShop.Web.csproj CoffeeShop.Web/
COPY CoffeeShop.Contracts/CoffeeShop.Contracts.csproj CoffeeShop.Contracts/
RUN dotnet restore CoffeeShop.Web/CoffeeShop.Web.csproj

COPY CoffeeShop.Web/ CoffeeShop.Web/
COPY CoffeeShop.Contracts/ CoffeeShop.Contracts/
RUN dotnet publish CoffeeShop.Web/CoffeeShop.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "CoffeeShop.Web.dll"]
