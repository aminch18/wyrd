# Built from the repository root (compose.yaml sets the context): every role runs this one image.
FROM mcr.microsoft.com/dotnet/sdk:10.0.201 AS build
WORKDIR /repo
COPY . .
RUN dotnet publish benchmarks/Wyrd.Benchmarks.Load/Wyrd.Benchmarks.Load.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
# Npgsql loads GSSAPI at startup; without the library it prints an error on every connection pool.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Wyrd.Benchmarks.Load.dll"]
