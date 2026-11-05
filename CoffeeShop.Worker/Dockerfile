FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CoffeeShop.Worker/CoffeeShop.Worker.csproj CoffeeShop.Worker/
COPY CoffeeShop.Contracts/CoffeeShop.Contracts.csproj CoffeeShop.Contracts/
RUN dotnet restore CoffeeShop.Worker/CoffeeShop.Worker.csproj

COPY CoffeeShop.Worker/ CoffeeShop.Worker/
COPY CoffeeShop.Contracts/ CoffeeShop.Contracts/
RUN dotnet publish CoffeeShop.Worker/CoffeeShop.Worker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CoffeeShop.Worker.dll"]
