﻿using Common.Logging;
using HD;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace Crypnostic.Internal
{
  /// <summary>
  /// https://coinmarketcap.com/api/
  /// 
  /// CoinMarketCap's API is a bit limited at the moment,
  /// coverting to another base currency does not work as well 
  /// as on the website.
  /// </summary>
  internal class CoinMarketCapAPI
  {
    #region Data
    internal protected readonly Dictionary<string, Coin>
      tickerLowerToCoin = new Dictionary<string, Coin>();

    static readonly ILog log = LogManager.GetLogger<CoinMarketCapAPI>();

    readonly Throttle throttle;

    readonly AutoUpdateWithThrottle autoUpdate;

    readonly IRestClient restClient;
    #endregion

    #region Init
    public CoinMarketCapAPI()
    {
      restClient = new RestClient("https://api.coinmarketcap.com");

      // Please limit requests to no more than 10 per minute.
      throttle = new Throttle(TimeSpan.FromMinutes(2 * 1 / 10), TimeSpan.FromMinutes(1));
      autoUpdate = new AutoUpdateWithThrottle(
        Refresh,
        TimeSpan.FromMinutes(10),
        throttle,
        CrypnosticController.instance.cancellationTokenSource.Token);
    }

    public async Task Start()
    {
      await autoUpdate.StartWithImmediateResults();
    }
    #endregion

    #region Helpers
    async Task Refresh()
    {
      (HttpStatusCode status, List<CoinMarketCapTickerJson> resultList)
        = await restClient.AsyncDownload<List<CoinMarketCapTickerJson>>("v1/ticker/?limit=0");
      if (status != HttpStatusCode.OK)
      {
        log.Error(status);
        throttle.BackOff();
        // TODO backoff if 400's
        return;
      }

      if (resultList == null)
      { // Parsing error, it may work on the next refresh
        log.Error("Refresh failed");
        return;
      }

      for (int i = 0; i < resultList.Count; i++)
      {
        CoinMarketCapTickerJson ticker = resultList[i];
        Debug.Assert(ticker != null);

        Coin coin = await CrypnosticController.instance.CreateFromName(ticker.name);
        if (coin == null)
        { // Blacklisted
          continue;
        }

        DateTime lastUpdated;
        if (long.TryParse(ticker.last_updated, out long secondsSince))
        {
          lastUpdated = DateTimeOffset.FromUnixTimeSeconds(secondsSince).DateTime;
        }
        else
        {
          lastUpdated = default(DateTime);
        }

        string symbol = ticker.symbol.ToLowerInvariant();

        if (tickerLowerToCoin.ContainsKey(symbol) == false)
        {
          tickerLowerToCoin[symbol] = coin;
        }

        coin.coinMarketCapData = new MarketCap(
          ticker.symbol,
          int.Parse(ticker.rank),
          ticker.price_btc.ToNullableDecimal(),
          ticker.price_usd.ToNullableDecimal(),
          ticker._24h_volume_usd.ToNullableDecimal(),
          ticker.market_cap_usd.ToNullableDecimal(),
          ticker.available_supply.ToNullableDecimal(),
          ticker.total_supply.ToNullableDecimal(),
          ticker.max_supply.ToNullableDecimal(),
          ticker.percent_change_1h.ToNullableDecimal(),
          ticker.percent_change_24h.ToNullableDecimal(),
          ticker.percent_change_7d.ToNullableDecimal(),
          lastUpdated);
      }
    }
    #endregion
  }
}
