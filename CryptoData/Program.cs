using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.Design;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exchange;
using HeimdallBase;
using Newtonsoft.Json;
using OsirisBase;
using UserLedger;

namespace CryptoData
{
    public class Program
    {
        public static void Main(string[] args) => new CryptoData().Start(args);
    }
    
    public class CryptoData : LedgerOsirisModule
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        private TradeManager TradeManager;

        public void Start(string[] args)
        {
            if (true)
            {
                var exampleShort = new FuturesPosition()
                {
                    Type = PositionType.Short,
                    Amount = 0.5m,
                    Collateral = 0.05m,
                    EntryPrice = 10000
                };
                
                // $10 in at 10x leverage and 50 price, $100 worth ($100 value, $10 collateral / 2 value 0.2 collateral)
                // 2 tokens must go down $10 in value to liquidate (price movement of $5)
                // movement * amount == collateral 
                
                var exampleLong = new FuturesPosition()
                {
                    Type = PositionType.Long,
                    Amount = 0.5m,
                    Collateral = 0.05m,
                    EntryPrice = 10000
                };
                
                Console.WriteLine($"Short liquidates at {exampleShort.GetLiquidationPrice()}");
                Console.WriteLine($"Long liquidates at {exampleLong.GetLiquidationPrice()}");
                
                Console.WriteLine();
                
                Console.WriteLine($"Short worth {exampleShort.GetValueAtPrice(exampleShort.GetLiquidationPrice())} at {exampleShort.GetLiquidationPrice()}");
                Console.WriteLine($"Long worth {exampleLong.GetValueAtPrice(exampleLong.GetLiquidationPrice())} at {exampleLong.GetLiquidationPrice()}");

                Console.WriteLine();
                Console.WriteLine($"Short worth {exampleShort.GetValueAtPrice(10900)} at ${10900}");
                Console.WriteLine($"Long worth {exampleLong.GetValueAtPrice(11250)} at ${11250}");
                
                Console.WriteLine();
                //
                // for (int p = 14000; p < 16000; p += 99)
                // {
                //     Console.WriteLine($"Short worth {exampleShort.GetValueAtPrice(p)} at ${p}");
                //     Console.WriteLine($"Long worth {exampleLong.GetValueAtPrice(p)} at ${p}");
                // }

                //return;
            }
            
            var strs = new List<(string, string)>();
            for (int i = 0; i < 9; i++)
            {
                for (int j = 20; j <= 40; j++)
                {
                    strs.Add(CryptoHandler.GetBar(j - 15, j - 5, 2 * j, 0));
                }
                for (int j = 40; j > 20; j--)
                {
                    strs.Add(CryptoHandler.GetBar(j - 5, j - 15, 2 * j, 0));
                }

                break;
            }

            CryptoHandler.PrintCandlesticks(strs);
            
            strs.Clear();
            
            for (int i = 0; i < 9; i++)
            {
                for (int j = 20; j <= 40; j++)
                {
                    strs.Add(CryptoHandler.GetBar(j - 10, j - 7, j, 0));
                }
                for (int j = 40; j > 20; j--)
                {
                    strs.Add(CryptoHandler.GetBar(j - 9, j - 10, j, 0));
                }

                break;
            }

            CryptoHandler.PrintCandlesticks(strs);

            //return;

            Name = "crypto";
            //SmartAlertManager.Load();

            Commands = new Dictionary<string, MessageHandler>()
            {
                {"", Wildcard },
                {".exc", GetExchange },
                {".statage ", GetOldestStat},
                {".stat ", GetNearestStat },
                {".graph ", PrintGraphToConsole},
                {".box ", PrintGraphToConsole},
                {".rsi ", PrintRsi},
                {"!trade ", HandleTrade},
                {"$flush", (a, s, n) => { SendMessage(string.Format("Saved {0} entries.", TickerDataManager.Save()), s); SendMessage(string.Format("Total cache entries: {0}", TickerDataManager.Buffers.Buffers.Sum(b => b.Value.Cached)), s); GC.Collect(); } }
            };

            Init(args, delegate
            {
                CryptoHandler.Init(!args.Contains("--create-buffers"));
                
                // if (false)
                //     TickerDataManager.Init(args.Contains("--create-buffers"));

                TradeManager = new TradeManager(this);
                TradeManager.Load();
                
                // while (true)
                // {
                //     Thread.Sleep(15000);
                //
                //     foreach (var exc in CryptoHandler.Exchanges)
                //     {
                //         if ((DateTime.Now - exc.Value.LastMessage).TotalSeconds > 25)
                //         {
                //             try
                //             {
                //                 Log.Debug($"Reconnecting to {exc.Key}...");
                //                 exc.Value.Reconnect();
                //             }
                //             catch
                //             {
                //             }
                //         }
                //     }
                // }
            });
        }

        void HandleTrade(string args, string source, string n)
        {
            var parts = args.Split(' ').Skip(1).ToArray();
            var verb = parts[0].ToLowerInvariant();

            parts = parts.Skip(1).ToArray();

            // .trade sell ETH 
            // .trade sell ETH 0.048327
            // .trade sell ETH $500
            // .trade sell ETH:BTC $500
            // .trade buy ETH 
            // .trade summary
            // .trade init

            string PrintBalances(List<TokenAmount> balances)
            {
                return string.Join(" | ",
                        balances.Where(balance => balance.Amount > 0).Select(balance => balance.ToString()))
                    + $" || Total equiv. balance: ${balances.Sum(b => b.UsdEquivalent):0.00}";
            }
            
            void PrintSummary(string nick)
            {
                var balances = TradeManager.DumpBalances(source, nick);
                TradeState state = null;
                var id = TradeManager.GetUserIdentifier(source, nick);

                if (TradeManager.UserStates.ContainsKey(id))
                {
                    state = TradeManager.UserStates[id];
                }

                var preamble = (nick == n) ? $"{nick}: " : $"{nick}'s balances: ";
                SendMessage(preamble + PrintBalances(balances) + (state != null ? $" || Cumulative PNL: ${state.CumulativePnl:0.00}" : ""), source);
            }
            
            switch (verb)
            {
                case "init":
                    var ret = TradeManager.InitUser(source, n);
                    
                    if (ret != null)
                        SendMessage(ret, source);
                    break;
                case "undo":
                {
                    var id = TradeManager.GetUserIdentifier(source, n);
                    if (TradeManager.OldStates.ContainsKey(id))
                    {
                        TradeManager.UserStates[id] = TradeManager.OldStates[id];
                        TradeManager.OldStates.Remove(id);
                        PrintSummary(n);
                    }
                    else
                    {
                        SendMessage("No undo is available for you.", source);
                    }
                    break;
                }
                case "leaderboard":
                case "losers":
                case "winners":
                {
                    bool order = verb == "leaderboard" || verb == "winners";
                    
                    var qualifyingPeople = TradeManager.UserStates.Where(s =>
                        Encoding.UTF8.GetString(Convert.FromBase64String(s.Key)).StartsWith(source));

                    // var peopleOrderedByTotalBalances =
                    //     qualifyingPeople.OrderByDescending(p =>
                    //         p.Value.DumpBalances().Sum(t => t.UsdEquivalent));

                    var peopleOrderedByPnl = (order ? qualifyingPeople.OrderByDescending(p => p.Value.CumulativePnl) : qualifyingPeople.OrderBy(p => p.Value.CumulativePnl)).ToList();

                    peopleOrderedByPnl = peopleOrderedByPnl.Where(p => p.Value.CumulativePnl != 0).ToList();

                    var take = 5;

                    if (parts.Any() && int.TryParse(parts[0], out int newTake))
                    {
                        take = newTake;
                    }

                    var topPeople = peopleOrderedByPnl.Take(take).ToList();

                    SendMessage($"{(order ? "Top" : "Bottom")} {topPeople.Count} traders:", source);

                    for (int i = 0; i < topPeople.Count; i++)
                    {
                        var person = Encoding.UTF8.GetString(Convert.FromBase64String(topPeople[i].Key)).Split('/')
                            .Last();
                        var bbbb = topPeople[i].Value.DumpBalances();
                        var totalusd = bbbb.Sum(b => b.UsdEquivalent);

                        SendMessage(
                            $"{i + 1}) {person[0]}\x03{person.Substring(1)}: \x02${topPeople[i].Value.CumulativePnl:0.00}\x02",
                            source); // // {string.Join(" | ", bbbb.Where(b => b.UsdEquivalent > 0.01m).Select(b => b.ToString()))}", source);
                    }

                    break;
                }
                case "state":
                    var t = n;
                    if (parts.Any())
                    {
                        t = parts[0];
                    }
                    
                    TradeManager.InitIfNotIn(source, t);
                    var bals = TradeManager.DumpBalances(source, t).Where(b => b.Amount > 0);
                    var obj = new {Nick = t, Balances = bals, Positions = TradeManager.UserStates[TradeManager.GetUserIdentifier(source, t)].FuturesPositions};
                    
                    SendMessage(JsonConvert.SerializeObject(obj), source);
                    break;
                case "summary":
                    var targetNick = n;
                    if (parts.Any())
                    {
                        targetNick = parts[0];
                    }
                    
                    TradeManager.InitIfNotIn(source, n);
                    PrintSummary(targetNick);
                    break;
                case "dump":
                    TradeManager.InitIfNotIn(source, n);
                    var balances = TradeManager.DumpBalances(source, n);

                    foreach (var b in balances)
                    {
                        if (string.Equals("USDT", b.Token, StringComparison.InvariantCultureIgnoreCase))
                            continue;

                        var result1 = TradeManager.DoTrade(source, n, b.Token, "USDT", b.Amount);

                        if (result1 != null)
                        {
                            TradeManager.DoTrade(source, n, b.Token, "BUSD", b.Amount);
                        }
                    }
                    
                    PrintSummary(n);
                    TradeManager.Save();
                    break;
                case "sell":
                case "buy":
                {
                    TradeManager.InitIfNotIn(source, n);
                    var tokenTarget = parts[0];
                    var isSelling = verb == "sell";
                    decimal maybeTokenAmount = -1;

                    if (parts.Length > 1)
                        maybeTokenAmount = decimal.Parse(parts[1]);

                    string leftToken, rightToken;

                    if (tokenTarget.Contains(':'))
                    {
                        leftToken = tokenTarget.Split(':')[0];
                        rightToken = tokenTarget.Split(':')[1];
                    }
                    else
                    {
                        rightToken = tokenTarget;
                        leftToken = "USDT";
                    }

                    if (isSelling)
                    {
                        (leftToken, rightToken) = (rightToken, leftToken);
                    }

                    var tradeResult = TradeManager.DoTrade(source, n, leftToken, rightToken, maybeTokenAmount);

                    if (tradeResult != null)
                    {
                        SendMessage(tradeResult, source);
                    }

                    PrintSummary(n);

                    TradeManager.Save();
                    break;
                }
                case "short":
                case "long":
                {
                    var type = Enum.Parse<PositionType>(verb, true);
                    var token = parts[0];

                    decimal _maybeTokenAmount = -1;
                    decimal leverage = 10;

                    if (parts.Length > 1)
                        _maybeTokenAmount = decimal.Parse(parts[1]);

                    if (parts.Length > 2)
                        leverage = decimal.Parse(parts[2].TrimEnd('x', 'X'));

                    var result =
                        TradeManager.OpenFuturesPosition(source, n, type, "USDT", token, _maybeTokenAmount, leverage);

                    if (result != null)
                        SendMessage(result, source);
                    TradeManager.Save();
                    break;
                }
                case "position":
                {
                    TradeManager.InitIfNotIn(source, n);
                    var id = TradeManager.GetUserIdentifier(source, n);
                    var state = TradeManager.UserStates[id];
                    var positions = state.FuturesPositions;

                    int i = int.Parse(parts[0]);

                    if (i <= 0 || i > positions.Count)
                    {
                        SendMessage($"You have {positions.Count} positions mate", source);
                        break;
                    }

                    i--;

                    var position = positions[i];
                    var cost = position.EntryPrice * position.Collateral;
                    var positionToken = position.Pair.Split(':')[0];
                    var currentPrice = (decimal) CryptoHandler.ConvertPrice(positionToken, "USDT", 1);
                    var currentValue = position.GetValueAtPrice(currentPrice);
                    var pnl = currentValue - cost;

                    SendMessage(
                        $"{n} position {i + 1}) {positionToken.ToUpperInvariant()} {position.Type.ToString().ToLowerInvariant()}, {position.Leverage}x leverage, {position.Amount:0.00} lots, entry ${position.EntryPrice} mark ${currentPrice} liq ${position.GetLiquidationPrice()}{(position.TakeProfit != 0 ? $" TP ${position.TakeProfit}" : "")}{(position.StopLoss != 0 ? $" SL ${position.StopLoss}" : "")}, unrealized PNL: ${pnl:0.00} ({pnl / cost:P})",
                        source);
                    break;
                }
                case "positions":
                {
                    TradeManager.InitIfNotIn(source, n);
                    var id = TradeManager.GetUserIdentifier(source, n);
                    var state = TradeManager.UserStates[id];
                    var positions = state.FuturesPositions;

                    SendMessage($"{n} has {positions.Count} positions:", source);

                    for (int i = 0; i < positions.Count; i++)
                    {
                        var position = positions[i];
                        var cost = position.EntryPrice * position.Collateral;
                        var positionToken = position.Pair.Split(':')[0];
                        var currentPrice = (decimal) CryptoHandler.ConvertPrice(positionToken, "USDT", 1);
                        var currentValue = position.GetValueAtPrice(currentPrice);
                        var pnl = currentValue - cost;

                        SendMessage(
                            $"{i + 1}) {positionToken.ToUpperInvariant()} {position.Type.ToString().ToLowerInvariant()}, {position.Leverage}x leverage, {position.Amount:0.00} lots, entry ${position.EntryPrice} mark ${currentPrice} liq ${position.GetLiquidationPrice()}{(position.TakeProfit != 0 ? $" TP ${position.TakeProfit}" : "")}{(position.StopLoss != 0 ? $" SL ${position.StopLoss}" : "")}, unrealized PNL: ${pnl:0.00} ({pnl / cost:P})",
                            source);
                    }
                    break;
                }
                case "close":
                {
                    TradeManager.InitIfNotIn(source, n);
                    var id = TradeManager.GetUserIdentifier(source, n);
                    var state = TradeManager.UserStates[id];
                    lock (state)
                    {
                        var positions = state.FuturesPositions;

                        if (!int.TryParse(parts[0], out int positionIndex) || positionIndex <= 0 ||
                            positionIndex > positions.Count)
                        {
                            SendMessage("What?", source);
                        }

                        var position = positions[positionIndex - 1];

                        var positionToken = position.Pair.Split(':')[0];
                        var currentPrice = (decimal) CryptoHandler.ConvertPrice(positionToken, "USDT", 1);
                        var positionValue = position.GetValueAtPrice(currentPrice);
                        var cost = position.EntryPrice * position.Collateral;

                        var setPrice = -1m;

                        if (parts.Length > 1 && decimal.TryParse(parts[1], out setPrice))
                        {
                            bool stopLoss = position.Type == PositionType.Short
                                ? setPrice > position.EntryPrice
                                : setPrice <= position.EntryPrice;

                            if (stopLoss)
                            {
                                position.StopLoss = setPrice;
                            }
                            else
                            {
                                position.TakeProfit = setPrice;
                            }

                            var pnl = position.GetValueAtPrice(setPrice) - cost;
                            SendMessage(
                                $"{n}: You set a {(stopLoss ? "stop loss" : "take profit")} price of ${setPrice} for position {positionIndex}. Projected {(pnl > 0 ? "profit" : "loss")}: ${Math.Abs(pnl):0.00} ({pnl / cost:P})",
                                source);
                        }
                        else
                        {
                            var pnl = positionValue - cost;

                            SendMessage(
                                $"{n}: You closed your position {positionToken.ToUpperInvariant()} {position.Type.ToString().ToLowerInvariant()}, {position.Leverage}x, {position.Amount} lots, entry ${position.EntryPrice:0.00} mark ${currentPrice:0.00}, realized {(pnl > 0 ? "profit" : "loss")}: ${Math.Abs(pnl):0.00} ({pnl / cost:P})",
                                source);

                            state.FuturesPositions.Remove(position);
                            TradeManager.PositionsWatched[positionToken.ToLowerInvariant()].Remove(position);
                            state.Balances["usdt"] += positionValue;
                            state.CumulativePnl += pnl;
                        }

                        TradeManager.Save();
                    }
                    break;
                }
                default:
                    SendMessage("Do one of init,sell,buy,summary,dump,leaderboard,state", source);
                    break;
            }
        }

        void PrintGraphToConsole(string args, string source, string n)
        {
            try
            {
                var isSmall = args.Split(' ')[0].EndsWith("box", StringComparison.InvariantCultureIgnoreCase);
                args = string.Join(' ', args.Split(' ').Skip(1));

                if (string.IsNullOrWhiteSpace(args) || args == "help")
                {
                    if (isSmall)
                    {
                        SendMessage("Syntax: .box <currency>[:<other currency>] [bar width]", source);
                        SendMessage("By default, other currency is USDT and bar width is 24h.", source);
                        return;
                    }
                    else
                    {
                        SendMessage("Syntax: .graph [fetch] <currency>[:<other currency>] [candle width]", source);
                        SendMessage("By default, other currency is USDT and candle width is 2m.", source);
                        return;
                    }
                }

                var parts = args.Split(' ');

                bool fetchRemote = true;
                
                if (parts[0] == "fetch")
                {
                    fetchRemote = true;
                    parts = parts.Skip(1).ToArray();
                }

                Ticker ticker = null;
                parts[0] = parts[0].ToUpper();

                if (parts[0].Contains(':'))
                {
                    if (parts[0].Contains('@'))
                        ticker = CryptoHandler.TickerFromString(parts[0]);
                    else
                        ticker = CryptoHandler.TickerFromString(parts[0] + "@Kucoin");
                }
                else
                {
                    ticker = CryptoHandler.TickerFromString(parts[0] + ":USDT@Kucoin");
                }

                //if (!CryptoHandler.Exchanges[].Contains(ticker.ToString()))
                

                var backupTicker = parts[0];
                bool tryYahoo = false;
                if (ticker != null)
                {
                    if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                    {
                        ticker = new Ticker(ticker.Second, ticker.First, ticker.Exchange);


                        if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                            ticker = new Ticker(ticker.First, ticker.Second, "Kucoin");
                        if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                            ticker = new Ticker(ticker.Second, ticker.First, "Kucoin");
                    }

                    if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                    {
                        tryYahoo = true;
                        // SendMessage($"I couldn't find the ticker you asked for.", source);
                        // return;
                    }
                }
                else
                {
                    tryYahoo = true;
                }

                int candle_w_sec = 120;
                int candle_count = 70;

                if (isSmall)
                    candle_w_sec = 86400;

                if (fetchRemote)
                    candle_w_sec = tryYahoo ? 300 : 180;

                string stringInterval = "";
                
                if (parts.Length > 1)
                {
                    if (fetchRemote)
                    {
                        if (tryYahoo)
                        {
                            if (!CryptoHandler.YahooCandlestickIntervals.ContainsValue(parts[1]))
                            {
                                SendMessage("When fetching from Yahoo, the following intervals are supported: " + string.Join(", ", CryptoHandler.YahooCandlestickIntervals.Values), source);
                                return;
                            }

                            candle_w_sec = CryptoHandler.YahooCandlestickIntervals.First(p => p.Value == parts[1]).Key;
                        }
                        else
                        {
                            if (!CryptoHandler.BinanceCandlestickIntervals.ContainsValue(parts[1]))
                            {
                                SendMessage("When fetching from Binance, the following intervals are supported: " + string.Join(", ", CryptoHandler.BinanceCandlestickIntervals.Values), source);
                                return;
                            }

                            candle_w_sec = CryptoHandler.BinanceCandlestickIntervals.First(p => p.Value == parts[1]).Key;   
                        }
                    }
                    else
                    {
                        if (char.IsDigit(parts[1][0]))
                        {
                            var len = int.Parse(new string(parts[1].TakeWhile(char.IsDigit).ToArray()));

                            if (parts[1].EndsWith("m"))
                            {
                                candle_w_sec = len * 60;
                            }
                            else if (parts[1].EndsWith("s"))
                            {
                                candle_w_sec = len;
                            }
                            else if (parts[1].EndsWith("h"))
                            {
                                candle_w_sec = len * 3600;
                            }
                        }
                    }
                }

                // if (candle_count * candle_w_sec > 10000)
                // {
                //     SendMessage("Note: you are viewing a bit too far into the past. Consider using narrower candles.",
                //         source);
                // }

                // if (ticker == null)
                // {
                //     SendMessage("Couldn't find that ticker.", source);
                //     return;
                // }

                var start = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(candle_w_sec * candle_count));
                start = start.AddSeconds(-start.Second);

                if (isSmall)
                {
                    var humanSmallTimeLabel = candle_w_sec <= 60 ? $"{candle_w_sec}s" :
                        candle_w_sec <= 90 * 60 ? $"{candle_w_sec / 60}m" :
                        candle_w_sec <= 86400 ? $"{candle_w_sec / 3600}h" : $"{candle_w_sec / 86400}d";
                    
                    var barWidth = 50;
                    var bar = $"├{new string('─', barWidth - 2)}┤".ToCharArray();
                    
                    var candleData = CryptoHandler.GrabCandlestickData(ticker,
                        TimeSpan.FromSeconds(candle_w_sec), 1, DateTime.UtcNow.AddSeconds(-candle_w_sec));
                    
                    (var entry, var exit, var low, var high) = candleData[0];
                    
                    Console.WriteLine($"{entry} {exit} {low} {high}");

                    var scale = high - low;
                    var movementWidth = (Math.Abs(entry - exit) / scale) * (barWidth - 2);
                    var movementStart = ((Math.Min(entry, exit) - low) / scale) * (barWidth - 2);
                    var movementUpwards = exit > entry;

                    var movementStartInt = (int) movementStart + 1;
                    var movementEndInt = (int) (movementStart + movementWidth);

                    bool neutral = movementStartInt == movementEndInt;

                    if (neutral)
                        movementEndInt++;

                    Console.WriteLine($"{scale} {movementStart} {movementWidth} {movementStartInt} {movementEndInt}");

                    for (int i = movementStartInt; i < movementEndInt && i < bar.Length; i++)
                    {
                        bar[i] = '■';
                    }

                    var barString = new string(bar);
                    var colorTag = (char) 3 + (neutral ? "00" : movementUpwards ? "03" : "04");

                    Console.WriteLine($"bar string {barString}");
                    barString = barString.Insert(movementStartInt, colorTag);
                    barString = barString.Insert(colorTag.Length + movementEndInt, "\u0003");
                    
                    Console.WriteLine($"bar string {barString}");
                    
                    string GetLabel(double price)
                    {
                        int digits_required = (int)Math.Abs(Math.Log10(Math.Abs(price)) - 5);
                        return Math.Round(price, digits_required).ToString("#,#,#0.########");
                    }

                    var lowLabel = $"{GetLabel(low)} ";
                    var highLabel = $" {GetLabel(high)}";
                    var entryLabel = GetLabel(entry);
                    var exitLabel = GetLabel(exit);

                    var entryLabelSize = entryLabel.Length;
                    var exitLabelSize = exitLabel.Length;

                    var entryPosition = lowLabel.Length + ((movementUpwards ? movementStartInt : movementEndInt) - entryLabelSize / 2);
                    var exitPosition = lowLabel.Length + ((movementUpwards ? movementEndInt : movementStartInt) - exitLabelSize / 2);
                    
                    var midpoint = low + (high - low) / 2;

                    var entryTack = $"t-{humanSmallTimeLabel}";
                    var exitTack = "now";

                    if (entry < midpoint)
                        entryLabel = $"{entryLabel} {entryTack}";
                    else
                    {
                        entryLabel = $"{entryTack} {entryLabel}";
                        entryPosition -= entryTack.Length + 1;
                    }
                    
                    exitTack =
                        $"(\u0003{(movementUpwards ? "3+" : "4")}{GetLabel(exit - entry)}\u0003 / \u0003{(movementUpwards ? "3+" : "4")}{(100d * (exit - entry)) / entry:0.0}%\u0003)";
                    
                    if (exit < midpoint)
                        exitLabel = $"{exitLabel} {exitTack}";
                    else
                    {
                        exitLabel = $"{exitTack} {exitLabel}";
                        exitPosition -= (exitTack.Length - 6) + 1;
                    }

                    barString = lowLabel + barString + highLabel;

                    var entryLine = new string(' ', barString.Length);
                    var exitLine = new string(' ', barString.Length);

                    
                    entryLine = entryLine.Remove(entryPosition, Math.Min(entryLine.Length - entryPosition, entryLabel.Length)).Insert(entryPosition, entryLabel);
                    exitLine = exitLine.Remove(exitPosition, Math.Min(exitLine.Length - exitPosition, exitLabel.Length)).Insert(exitPosition, exitLabel);
                    
                    SendMessage(entryLine, source);
                    SendMessage(barString, source);
                    SendMessage(exitLine, source);
                }
                else
                {
                    List<(double, double, double, double)> candleData = null;

                    double latestPrice = 0;
                    TickerData latestTickerData = null;
                    
                    if (fetchRemote)
                    {
                        if (tryYahoo)
                        {
                            (latestTickerData, candleData) =
                                CryptoHandler.GrabCandlestickDataFromYahoo(backupTicker, CryptoHandler.YahooCandlestickIntervals[candle_w_sec], start, candle_count);
                            while (candleData.Count > candle_count)
                                candleData.RemoveAt(0);
                            
                        }
                        else
                            candleData = CryptoHandler.GrabCandlestickDataFromBinance(ticker,
                                TimeSpan.FromSeconds(candle_w_sec), candle_count, start);
                    }
                    else
                    {
                        candleData = CryptoHandler.GrabCandlestickData(ticker,
                            TimeSpan.FromSeconds(candle_w_sec), candle_count, start);
                    }

                    (var candlesticks, var top, var scale) = CryptoHandler.RenderCandlesticks(candleData);

                    if (!tryYahoo)
                    {
                        latestTickerData =
                            CryptoHandler.GetCurrentTickerData(CryptoHandler.Exchanges[ticker.Exchange], ticker);
                    }
                    latestPrice = latestTickerData.LastTrade;

                    CryptoHandler.PrintCandlesticks(candlesticks);
                    var lines = CryptoHandler.IrcPrintCandlesticks(candlesticks, top, scale, candle_w_sec,
                        latestPrice);

                    foreach (var line in lines)
                    {
                        // SendMessage(line, source);
                        // Thread.Sleep(15);
                    }
                    
                    SendMessage(string.Join('\0', lines), source);

                    if (!tryYahoo)
                        GetExchange($".exc 1 {ticker.First} to {ticker.Second}", source, n);
                    else
                    {
                        var yahooTickerDetails = CryptoHandler.YahooTickers[latestTickerData.Ticker.First];
                        var priceDisplay = yahooTickerDetails.QuoteCurrency == "USD"
                            ? $"${latestTickerData.LastTrade:0.00}" : $"{latestTickerData.LastTrade:0.00} {yahooTickerDetails.QuoteCurrency}";
                        var actualChangeAmount =
                            Math.Abs(latestTickerData.LastTrade * latestTickerData.DailyChangePercentage);
                        
                        SendMessage($"{yahooTickerDetails.LongName} ({yahooTickerDetails.Symbol}): {priceDisplay} {string.Format(latestTickerData.DailyChangePercentage > 0 ? "03+{1:0.00} (03{0:0.##}%)" : "04-{1:0.00} (04{0:0.##}%)", latestTickerData.DailyChangePercentage * 100, actualChangeAmount)}", source);
                    }
                }
            }
            catch (Exception ex)
            {
                SendMessage($"Something went wrong: {ex.Message}", source);
                Console.WriteLine(ex);
            }
        }

        void PrintRsi(string args, string source, string n)
        {
            try
            {
                var isSmall = false;
                args = string.Join(' ', args.Split(' ').Skip(1));

                if (string.IsNullOrWhiteSpace(args) || args == "help")
                {
                    SendMessage("Syntax: .rsi <currency>[:<other currency>] [point width]", source);
                    SendMessage("By default, other currency is USDT and candle width is 2m.", source);
                    return;
                }

                var parts = args.Split(' ');

                bool fetchRemote = true;

                Ticker ticker = null;
                parts[0] = parts[0].ToUpper();

                if (parts[0].Contains(':'))
                {
                    if (parts[0].Contains('@'))
                        ticker = CryptoHandler.TickerFromString(parts[0]);
                    else
                        ticker = CryptoHandler.TickerFromString(parts[0] + "@Kucoin");
                }
                else
                {
                    ticker = CryptoHandler.TickerFromString(parts[0] + ":USDT@Kucoin");
                }

                var backupTicker = parts[0];
                bool tryYahoo = false;
                if (ticker != null)
                {
                    if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                    {
                        ticker = new Ticker(ticker.Second, ticker.First, ticker.Exchange);


                        if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                            ticker = new Ticker(ticker.First, ticker.Second, "Kucoin");
                        if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                            ticker = new Ticker(ticker.Second, ticker.First, "Kucoin");
                    }

                    if (!CryptoHandler.Exchanges[ticker.Exchange].Tickers.Contains(ticker))
                    {
                        tryYahoo = true;
                        // SendMessage($"I couldn't find the ticker you asked for.", source);
                        // return;
                    }
                }
                else
                {
                    tryYahoo = true;
                }

                int candle_w_sec;
                int candle_count = 70;

                candle_w_sec = tryYahoo ? 300 : 180;

                string stringInterval = "";
                
                if (parts.Length > 1)
                {
                    if (tryYahoo)
                    {
                        if (!CryptoHandler.YahooCandlestickIntervals.ContainsValue(parts[1]))
                        {
                            SendMessage("When fetching from Yahoo, the following intervals are supported: " + string.Join(", ", CryptoHandler.YahooCandlestickIntervals.Values), source);
                            return;
                        }

                        candle_w_sec = CryptoHandler.YahooCandlestickIntervals.First(p => p.Value == parts[1]).Key;
                    }
                    else
                    {
                        if (!CryptoHandler.BinanceCandlestickIntervals.ContainsValue(parts[1]))
                        {
                            SendMessage("When fetching from Binance, the following intervals are supported: " + string.Join(", ", CryptoHandler.BinanceCandlestickIntervals.Values), source);
                            return;
                        }

                        candle_w_sec = CryptoHandler.BinanceCandlestickIntervals.First(p => p.Value == parts[1]).Key;   
                    }
                }

                var rsiPeriods = 14;
                candle_count += (rsiPeriods * 2);
                var start = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(candle_w_sec * candle_count));
                start = start.AddSeconds(-start.Second);
                
                // RSI params
                //start = start - (TimeSpan.FromSeconds(candle_w_sec * rsiPeriods));
                

                {
                    List<(double, double, double, double)> candleData = null;

                    double latestPrice = 0;
                    TickerData latestTickerData = null;
                    
                    if (tryYahoo)
                    {
                        (latestTickerData, candleData) =
                            CryptoHandler.GrabCandlestickDataFromYahoo(backupTicker, CryptoHandler.YahooCandlestickIntervals[candle_w_sec], start, candle_count);
                        while (candleData.Count > candle_count)
                            candleData.RemoveAt(0);
                        
                    }
                    else
                        candleData = CryptoHandler.GrabCandlestickDataFromBinance(ticker,
                            TimeSpan.FromSeconds(candle_w_sec), candle_count, start);

                    (var rsiData, var maxR, var scaleR) = CryptoHandler.TransformRSI(candleData, rsiPeriods);
                    (var candlesticks, var top, var scale) = CryptoHandler.RenderRSI(rsiData, maxR, scaleR);

                    if (!tryYahoo)
                    {
                        latestTickerData =
                            CryptoHandler.GetCurrentTickerData(CryptoHandler.Exchanges[ticker.Exchange], ticker);
                    }
                    latestPrice = latestTickerData.LastTrade;

                    CryptoHandler.PrintCandlesticks(candlesticks);
                    var lines = CryptoHandler.IrcPrintCandlesticks(candlesticks, top, scale, candle_w_sec,
                        latestPrice, printPercentageAxis: false);

                    SendMessage(string.Join('\0', lines), source);

                    if (!tryYahoo)
                        GetExchange($".exc 1 {ticker.First} to {ticker.Second}", source, n);
                    else
                    {
                        var yahooTickerDetails = CryptoHandler.YahooTickers[latestTickerData.Ticker.First];
                        var priceDisplay = yahooTickerDetails.QuoteCurrency == "USD"
                            ? $"${latestTickerData.LastTrade:0.00}" : $"{latestTickerData.LastTrade:0.00} {yahooTickerDetails.QuoteCurrency}";
                        var actualChangeAmount =
                            Math.Abs(latestTickerData.LastTrade * latestTickerData.DailyChangePercentage);
                        
                        SendMessage($"{yahooTickerDetails.LongName} ({yahooTickerDetails.Symbol}): {priceDisplay} {string.Format(latestTickerData.DailyChangePercentage > 0 ? "03+{1:0.00} (03{0:0.##}%)" : "04-{1:0.00} (04{0:0.##}%)", latestTickerData.DailyChangePercentage * 100, actualChangeAmount)}", source);
                    }
                }
            }
            catch (Exception ex)
            {
                SendMessage($"Something went wrong: {ex.Message}", source);
                Console.WriteLine(ex);
            }
        }

        void GetNearestStat(string args, string source, string n)
        {
            args = args.Substring(".stat".Length).Trim();
            var ticker = CryptoHandler.TickerFromString(args.Split(' ')[0].Trim());
            var time_str = string.Join(" ", args.Split(' ').Skip(1));
            var time = TimeUtils.Get(time_str, true);
            var data = TickerDataManager.GetTickerDataForTime(ticker, time);
            
            SendMessage(string.Format("Ticker data for {0}: Requested age: {4}, {1:##,#0.########}, {2} old, {3}, delta {5}", ticker, data.LastTrade, Utilities.TimeSpanToPrettyString(DateTime.Now - data.Timestamp), data.Timestamp, time, time - data.Timestamp), source);
        }

        void GetOldestStat(string args, string source, string n)
        {
            args = args.Substring(".statage".Length).Trim();
            var ticker = CryptoHandler.TickerFromString(args);
            Log.Info("Ticker for .statage: {0}", ticker);
            var data = TickerDataManager.GetOldestTickerData(ticker, out int rawtime, out int index);
            Log.Info("Found data for {0}", ticker);

            SendMessage(
                $"Oldest ticker data for {ticker}: {data.LastTrade:##,#0.########}, {Utilities.TimeSpanToPrettyString(DateTime.Now - data.Timestamp)} old, {data.Timestamp}, {rawtime}, index {index}", source);
        }

        void Wildcard(string args, string source, string n)
        {
            var prefix = ".";
            
            if (Config.Contains("crypto.disabled", source))
                return;

            if (Config.Contains("crypto.alter", source))
                prefix = "!";
            
		    if (args.StartsWith("!chat") || args.StartsWith("!trade"))
		    {
			    return;
		    }

            if (args.StartsWith(prefix))
            {
                var ticker = args.Substring(1);
                var first = ticker.Split(' ')[0];

                if (CryptoHandler.Tickers.Contains(first.ToUpper()))
                {
                    var rest = ticker.Split(' ').Skip(1).Select(t => t.Trim()).ToArray();

                    //Console.WriteLine("\"{0}\"", rest[0]);

                    if (rest.Any() && CryptoHandler.LooksLikeAddress(rest[0]))
                    {
                        try
                        {
                            SendMessage(CryptoHandler.GetAddressInfo(rest[0]), source);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }

                    if (rest.Any() && CryptoHandler.LooksLikeTxid(rest[0]))
                    {
                        try
                        {
                            SendMessage(CryptoHandler.GetTransactionInfo(rest[0]), source);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }

                    if (rest.Any() && (rest[0] == "block" || rest[0] == "latest" || rest[0] == "latestblock" || rest[0] == "last" || rest[0] == "lastblock"))
                    {
                        try
                        {
                            SendMessage(CryptoHandler.GetLastBlockInfo(), source);
                            return;
                        }
                        catch
                        {

                        }
                    }

                    if (rest.Any() && (CryptoHandler.Tickers.Contains(rest[0].ToUpper())))
                    {
                        GetExchange(string.Format(".exc 1 {0} to {1}", first, rest[0]), source, n);
                        return;
                    }

                    double amount = 1;
                    bool to_usd = true;
                    bool success = false;

                    for (int i = 0; i < rest.Length; i++)
                    {
                        if (rest[i].StartsWith("$") && (success = double.TryParse(rest[i].Substring(1), out amount)))
                        {
                            to_usd = false;
                            break;
                        }
                        else if ((success = double.TryParse(rest[i], out amount)))
                            break;
                    }

                    if (amount == 0)
                        amount = 1;

                    if (to_usd)
                        GetExchange(string.Format(".exc {0} {1} to USDT", amount, first), source, n);
                    else
                        GetExchange(string.Format(".exc {0} USDT to {1}", amount, first), source, n);
                }
            }
        }

        public void GetExchange(string args, string source, string n)
        {
            args = args.Substring(".exc".Length).Trim();
            var parts = args.Split(' ');

            var helpText =
                "Sorry, I couldn't understand that. Try something like .exc 100 usd to btc. Known currencies are: " +
                string.Join(", ", CryptoHandler.Tickers);

            if (helpText.Length > 1024)
                helpText = helpText.Substring(0, 1024) + "...";
            
            if (!parts.Any(p => double.TryParse(p, out double tmp)))
            {
                SendMessage(helpText, source);
                return;
            }

            var amount = double.Parse(parts.First(p => double.TryParse(p, out double tmp)));

            var eligible = parts.Where(p => CryptoHandler.Tickers.Contains(p.ToUpper())).ToList();

            if (eligible.Count < 2)
            {
                SendMessage(helpText, source);
                return;
            }

            try
            {
                SendMessage(CryptoHandler.Convert(eligible[0], eligible[1], amount), source);
            }
            catch (Exception ex)
            {
                SendMessage(string.Format("Exception occurred: {0}", ex.Message), source);
            }
        }
    }
}
