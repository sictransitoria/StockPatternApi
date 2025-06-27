# Stock Pattern API

ASP.NET Core API to detect wedge patterns in stock data from Alpha Vantage.

## Features

- `GET /api/Stock/getSetups`: Detects wedge patterns for NASDAQ tickers.
- Query params: `tickers` (e.g., AAPL,MSFT), `period` (1mo, 3mo, 6mo), `lookback` (default: 10).

## Setup

1. Clone: `git clone <repo-url>`
2. Set Alpha Vantage API key in `ApiKeys.API_KEY`.
3. Run: `dotnet restore && dotnet run`

## Example

`curl "https://localhost:5001/api/Stock/getSetups?tickers=AAPL"`

## Requirements

- .NET 8.0+
- Alpha Vantage API key
