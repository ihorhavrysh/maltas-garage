# Malta's Garage

[![CI/CD](https://github.com/ihorhavrysh/maltas-garage/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/ihorhavrysh/maltas-garage/actions/workflows/ci-cd.yml)

A peer-to-peer marketplace for expats in Malta to buy and sell second-hand items.

## Features

- Smart pricing with auction mode
- Escrow payment protection via Stripe
- MaltaPost and hand-to-hand delivery options
- Review and rating system

## Tech Stack

- ASP.NET Core 8
- Entity Framework Core
- SQL Server
- Bootstrap 5
- Stripe Connect

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server / LocalDB

### Running the application

```bash
dotnet run --project src/MaltasGarage.Web
```

### Running tests

```bash
dotnet test
```

## Architecture

This project follows Clean Architecture principles:

- **Domain** - Entities, Enums, Domain Events
- **Application** - Business logic, Interfaces, DTOs
- **Infrastructure** - Data access, External services
- **Web** - Razor Pages, Controllers, Views

## License

Private project.
