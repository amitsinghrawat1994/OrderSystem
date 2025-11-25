FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["OrderSystem.Api/OrderSystem.Api.csproj", "OrderSystem.Api/"]
COPY ["OrderSystem.Shared/OrderSystem.Shared.csproj", "OrderSystem.Shared/"]
RUN dotnet restore "OrderSystem.Api/OrderSystem.Api.csproj"
COPY . .
WORKDIR "/src/OrderSystem.Api"
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderSystem.Api.dll"]