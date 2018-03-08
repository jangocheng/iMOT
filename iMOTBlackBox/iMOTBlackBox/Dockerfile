FROM microsoft/aspnetcore:2.0 AS base
WORKDIR /app

FROM microsoft/dotnet:2.0-sdk AS build
WORKDIR /src
COPY *.sln ./
COPY iMOTBlackBox/iMOTBlackBox.csproj iMOTBlackBox/
RUN dotnet restore
COPY . .
WORKDIR /src/iMOTBlackBox
RUN dotnet build -c Release -o /app

FROM build AS publish
RUN dotnet publish -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENV ASPNETCORE_ENVIRONMENT Production
ENTRYPOINT ["dotnet", "iMOTBlackBox.dll"]