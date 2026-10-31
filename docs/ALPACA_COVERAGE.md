# Alpaca API coverage

Every Alpaca **Trading API** and **Market Data API** REST endpoint, mapped to the `idiotproof`
operation (CLI command = MCP tool) that reaches it through the Hub. Orders, replaces, cancels,
position closes, exercises and crypto orders all go through the TransactionGateway (Live from the
CLI or MCP returns a preview and a confirmation code); funding and transfers always need a code.

The inventory below is checked by `AlpacaCoverageTests` (IdiotProof.Operations.Tests): every row
must name at least one operation that exists in the catalog, and every Alpaca path the operations
call must appear here. Add a row when you add an endpoint; add an operation when you add a row.

Out of scope: the **Broker API** (for broker-dealers running accounts for their own customers; IdiotProof
trades its owner's own accounts), **OAuth Connect** (already wired in the web app's
`/connect/alpaca` flow) and the **crypto perpetual futures** data API. The **WebSocket streams** are
covered by the streaming operations listed after the inventory.

Any endpoint not yet listed is still reachable with `alpaca raw <METHOD> <path> [--body json] [--data] [--mode]`
(Trading API, or a Market Data GET with `--data`). It refuses every mutation under `/v2/orders`,
`/v2/positions` (closes, exercise, do-not-exercise) and `/v2/wallets`, naming the gated operation to use,
and a Live mutation of anything else needs a confirmation code bound to the exact request.

<!-- inventory:start -->

## Trading API: account

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v2/account | `account summary`, `account buying-power`, `account pdt`, `options level`, `keys probe` | Paper and Live side by side (`--mode`). |
| GET | /v2/account/configurations | `account config` | |
| PATCH | /v2/account/configurations | `account config set` | Live needs a confirmation code bound to the change. |
| GET | /v2/account/activities | `account activities` | Cash flows also feed the deposit-free P&L in `account summary`/`account history`. |
| GET | /v2/account/activities/{activity_type} | `account activities` | `--type` with one type. |
| GET | /v2/account/portfolio/history | `account history` | Braille equity chart, ET sessions. |
| GET | /v2/clock | `account clock` | |
| GET | /v2/calendar | `account calendar` | |

## Trading API: orders

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| POST | /v2/orders | `orders place`, `orders bracket`, `orders oco`, `orders oto`, `trade buy`, `trade sell`, `trade short`, `trade cover`, `trade notional`, `options buy`, `options sell`, `options spread`, `crypto buy`, `crypto sell` | Always through the TransactionGateway. |
| GET | /v2/orders | `orders list`, `orders history` | Nested legs shown. |
| DELETE | /v2/orders | `orders cancel-all` | Gateway; Live needs a code. |
| GET | /v2/orders/{order_id} | `orders show` | |
| GET | /v2/orders:by_client_order_id | `orders show` | `--client-id`. |
| PATCH | /v2/orders/{order_id} | `orders replace` | Code bound to the exact change. |
| DELETE | /v2/orders/{order_id} | `orders cancel` | |

## Trading API: positions

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v2/positions | `positions list`, `positions reconcile`, `options positions`, `crypto positions` | |
| GET | /v2/positions/{symbol_or_asset_id} | `positions show` | |
| DELETE | /v2/positions | `positions close-all` | `--preview` lists without sending. |
| DELETE | /v2/positions/{symbol_or_asset_id} | `positions close`, `options close`, `trade flatten` | Qty or percentage. |
| POST | /v2/positions/{symbol_or_contract_id}/exercise | `options exercise` | |
| POST | /v2/positions/{symbol_or_contract_id}/do-not-exercise | `options do-not-exercise` | |

## Trading API: assets, options contracts, corporate actions

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v2/assets | `assets list` | |
| GET | /v2/assets/{symbol_or_asset_id} | `assets show` | |
| GET | /v2/assets/fixed_income/us_treasuries | `assets bonds` | |
| GET | /v2/options/contracts | `options contracts`, `options chain` | |
| GET | /v2/options/contracts/{symbol_or_id} | `options contract` | |
| GET | /v2/corporate_actions/announcements | `corporate-actions list` | Deprecated by Alpaca in favor of /v1/corporate-actions; both read. |
| GET | /v2/corporate_actions/announcements/{id} | `corporate-actions show` | |

## Trading API: watchlists

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v2/watchlists | `watchlists list` | |
| POST | /v2/watchlists | `watchlists create` | |
| GET | /v2/watchlists/{watchlist_id} | `watchlists show` | |
| PUT | /v2/watchlists/{watchlist_id} | `watchlists rename` | |
| POST | /v2/watchlists/{watchlist_id} | `watchlists add` | |
| DELETE | /v2/watchlists/{watchlist_id} | `watchlists delete` | |
| DELETE | /v2/watchlists/{watchlist_id}/{symbol} | `watchlists remove` | |
| GET | /v2/watchlists:by_name | `watchlists show` | A name resolves by name, an id by id. |
| PUT | /v2/watchlists:by_name | `watchlists rename` | Served by the id form after the name lookup. |
| POST | /v2/watchlists:by_name | `watchlists add` | Served by the id form after the name lookup. |
| DELETE | /v2/watchlists:by_name | `watchlists delete` | Served by the id form after the name lookup. |

## Trading API: crypto funding

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v2/wallets | `crypto wallets` | |
| GET | /v2/wallets/transfers | `crypto transfers` | |
| GET | /v2/wallets/transfers/{transfer_id} | `crypto transfers` | `--id`. |
| POST | /v2/wallets/transfers | `crypto withdraw` | Always needs a confirmation code. |
| GET | /v2/wallets/whitelists | `crypto whitelist` | |
| POST | /v2/wallets/whitelists | `crypto whitelist-add` | Always needs a confirmation code. |
| DELETE | /v2/wallets/whitelists/{whitelisted_address_id} | `crypto whitelist-remove` | Always needs a confirmation code. |
| GET | /v2/wallets/fees/estimate | `crypto fee-estimate` | |

## Market Data API: stocks

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v2/stocks/bars | `market bars`, `options signals` | Multi-symbol. |
| GET | /v2/stocks/{symbol}/bars | `market bars`, `market chart`, `market candlesticks` | |
| GET | /v2/stocks/bars/latest | `market latest` | `market latest bar`. |
| GET | /v2/stocks/{symbol}/bars/latest | `market latest` | Served by the multi-symbol form. |
| GET | /v2/stocks/quotes | `market quotes` | |
| GET | /v2/stocks/{symbol}/quotes | `market quotes` | |
| GET | /v2/stocks/quotes/latest | `market quote`, `market latest` | |
| GET | /v2/stocks/{symbol}/quotes/latest | `market quote` | Served by the multi-symbol form. |
| GET | /v2/stocks/trades | `market trades` | |
| GET | /v2/stocks/{symbol}/trades | `market trades` | |
| GET | /v2/stocks/trades/latest | `market quote`, `market latest` | |
| GET | /v2/stocks/{symbol}/trades/latest | `market quote`, `options chain` | |
| GET | /v2/stocks/snapshots | `market snapshot`, `market prev-close`, `market scan-universe` | |
| GET | /v2/stocks/{symbol}/snapshot | `market snapshot` | Served by the multi-symbol form. |
| GET | /v2/stocks/auctions | `market auctions` | |
| GET | /v2/stocks/{symbol}/auctions | `market auctions` | Served by the multi-symbol form. |
| GET | /v2/stocks/meta/conditions/{ticktype} | `market conditions` | |
| GET | /v2/stocks/meta/exchanges | `market exchanges` | |

## Market Data API: options

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v1beta1/options/bars | `options bars`, `options signals` | |
| GET | /v1beta1/options/trades | `options trades` | |
| GET | /v1beta1/options/trades/latest | `options latest` | |
| GET | /v1beta1/options/quotes/latest | `options latest`, `options quote` | |
| GET | /v1beta1/options/snapshots | `options snapshot`, `options quote`, `options greeks`, `options iv` | |
| GET | /v1beta1/options/snapshots/{underlying_symbol} | `options chain` | |
| GET | /v1beta1/options/meta/conditions/{ticktype} | `options conditions` | |
| GET | /v1beta1/options/meta/exchanges | `options exchanges` | |

## Market Data API: crypto

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v1beta3/crypto/{loc}/bars | `crypto bars`, `market chart` | `loc` = us. |
| GET | /v1beta3/crypto/{loc}/latest/bars | `crypto latest` | |
| GET | /v1beta3/crypto/{loc}/latest/orderbooks | `crypto orderbook` | |
| GET | /v1beta3/crypto/{loc}/latest/quotes | `crypto latest` | |
| GET | /v1beta3/crypto/{loc}/latest/trades | `crypto latest` | |
| GET | /v1beta3/crypto/{loc}/quotes | `crypto quotes` | |
| GET | /v1beta3/crypto/{loc}/snapshots | `crypto snapshot` | |
| GET | /v1beta3/crypto/{loc}/trades | `crypto trades` | |

## Market Data API: news, screener, logos, corporate actions, forex, fixed income

| Method | Endpoint | Operation | Notes |
|---|---|---|---|
| GET | /v1beta1/news | `market news` | |
| GET | /v1beta1/screener/stocks/most-actives | `market most-actives` | |
| GET | /v1beta1/screener/{market_type}/movers | `market movers`, `scan movers`, `gapper premarket-fade` | `market_type` = stocks. |
| GET | /v1beta1/logos/{symbol} | `market logo` | |
| GET | /v1/corporate-actions | `corporate-actions list` | |
| GET | /v1beta1/forex/rates | `market fx` | |
| GET | /v1beta1/forex/latest/rates | `market fx` | |
| GET | /v1beta1/fixed_income/latest/prices | `market bond-prices` | |

<!-- inventory:end -->

## WebSocket streams

Hosted in the Hub; the CLI streams them live (Ctrl+C stops), MCP and `--json` get a bounded window.

| Stream | Endpoint | Operation | Notes |
|---|---|---|---|
| Trade updates | wss://paper-api.alpaca.markets/stream, wss://api.alpaca.markets/stream (`trade_updates`) | `orders stream` | One listener per account, kept until the Hub stops; every fill and partial fill is written to the audit log (`fill`) and the trade diary. |
| Stocks | wss://stream.data.alpaca.markets/v2/{sip,iex,delayed_sip} | `market watch` | Trades, quotes, bars, daily and updated bars, trading statuses, LULD. The Monitor holds its own stocks socket for trades and minute bars. |
| Crypto | wss://stream.data.alpaca.markets/v1beta3/crypto/us | `market watch` | Trades, quotes, bars. |
| Options | wss://stream.data.alpaca.markets/v1beta1/{opra,indicative} | `market watch` | Trades and quotes; MessagePack frames. |
| News | wss://stream.data.alpaca.markets/v1beta1/news | `market watch --news` | Headlines for the symbols, or all news. |
| (health) | every socket above | `market stream status` | State, connected since, last message, reconnects, errors. |
