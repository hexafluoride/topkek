using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Exchange;
using Newtonsoft.Json;

namespace CryptoData
{
    public class TradeManager
    {
        public Dictionary<string, TradeState> UserStates = new();
        private const string StateFile = "balances.json";
        
        public Dictionary<string, List<FuturesPosition>> PositionsWatched = new();

        private CryptoData CryptoData;

        public TradeManager(CryptoData instance)
        {
            CryptoData = instance;
            
            foreach (var exchange in CryptoHandler.Exchanges)
            {
                exchange.Value.OnTickerUpdateReceived += CheckLiquidation;
            }
        }

        private void CheckLiquidation(IExchange instance, TickerData data)
        {
            //Console.WriteLine($"Received ticker update for {data.Ticker.First}:{data.Ticker.Second}: price {data.LastTrade}");
            
            if (data.Ticker.First != "USDT" && data.Ticker.Second != "USDT")
                return;

            var otherToken = data.Ticker.First == "USDT" ? data.Ticker.Second : data.Ticker.First;
            otherToken = otherToken.ToLowerInvariant();
            var price = data.Ticker.First == "USDT" ? 1 / data.LastTrade : data.LastTrade;

            if (!PositionsWatched.ContainsKey(otherToken))
            {
                return;
            }

            var positionsList = PositionsWatched[otherToken];
            bool anyLiquidated = false;
            
            for (int i = 0; i < positionsList.Count; i++)
            {
                var position = positionsList[i];
                bool liquidated = false;
                bool stopLoss = false;
                bool takeProfit = false;
                var state = UserStates[position.UserId];

                lock (state)
                {

                    if (position.Type == PositionType.Long)
                    {
                        liquidated = price <= (double) position.GetLiquidationPrice();

                        if (!liquidated)
                        {
                            if (position.StopLoss != 0)
                                stopLoss = price <= (double) position.StopLoss;
                            if (position.TakeProfit != 0)
                                takeProfit = price >= (double) position.TakeProfit;
                        }
                    }
                    else if (position.Type == PositionType.Short)
                    {
                        liquidated = price >= (double) position.GetLiquidationPrice();

                        if (!liquidated)
                        {
                            if (position.StopLoss != 0)
                                stopLoss = price >= (double) position.StopLoss;
                            if (position.TakeProfit != 0)
                                takeProfit = price <= (double) position.TakeProfit;
                        }
                    }

                    //Console.WriteLine($"{position.Type} position {i} {(liquidated ? "liquidated" : "not liquidated")} at {price} (liquidation is {position.GetLiquidationPrice()})");

                    if (liquidated)
                    {
                        anyLiquidated = true;
                        try
                        {
                            var positionToken = position.Pair.Split(':')[0];
                            var currentPrice = (decimal) CryptoHandler.ConvertPrice(positionToken, "USDT", 1);
                            var positionValue = position.GetValueAtPrice(currentPrice);
                            var cost = position.EntryPrice * position.Collateral;
                            var pnl = positionValue - cost;
                            state.FuturesPositions.Remove(position);
                            state.CumulativePnl += pnl;

                            state.Balances["usdt"] += Math.Max(0, positionValue);
                            positionsList.RemoveAt(i);
                            var sourceAndNick = Encoding.UTF8.GetString(Convert.FromBase64String(position.UserId));
                            var parts = sourceAndNick.Split('/');
                            var source = string.Join('/', parts.AsSpan(0, parts.Length - 1).ToArray());
                            var nick = parts.Last();


                            CryptoData.SendMessage(
                                $"{nick}: You just got fucking liquidated buddy. It's over ({position.Leverage}x {position.Pair.ToUpperInvariant()} {position.Type.ToString().ToLowerInvariant()}, lost {position.Collateral} {otherToken} (${position.Collateral * (decimal) price:0.00}))",
                                source);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                            throw;
                        }
                    }

                    if (stopLoss || takeProfit)
                    {
                        var sourceAndNick = Encoding.UTF8.GetString(Convert.FromBase64String(position.UserId));
                        var parts = sourceAndNick.Split('/');
                        var source = string.Join('/', parts.AsSpan(0, parts.Length - 1).ToArray());
                        var nick = parts.Last();

                        var positionToken = position.Pair.Split(':')[0];
                        var currentPrice = (decimal) CryptoHandler.ConvertPrice(positionToken, "USDT", 1);
                        var positionValue = position.GetValueAtPrice(currentPrice);
                        var cost = position.EntryPrice * position.Collateral;
                        var pnl = positionValue - cost;

                        CryptoData.SendMessage(
                            $"{nick}: Your position hit {(stopLoss ? "stop loss" : "take profit")}: {positionToken.ToUpperInvariant()} {position.Type.ToString().ToLowerInvariant()}, {position.Leverage}x, {position.Amount} lots, entry ${position.EntryPrice:0.00} mark ${currentPrice:0.00}, realized {(pnl > 0 ? "profit" : "loss")}: ${Math.Abs(pnl):0.00}",
                            source);

                        state.FuturesPositions.Remove(position);
                        positionsList.RemoveAt(i);
                        state.Balances["usdt"] += positionValue;
                        state.CumulativePnl += pnl;
                        anyLiquidated = true;
                    }
                }
            }

            if (anyLiquidated)
                Save();
        }

        public void Load()
        {
            if (!File.Exists(StateFile))
                return;

            try
            {
                UserStates = JsonConvert.DeserializeObject<Dictionary<string, TradeState>>(File.ReadAllText(StateFile));
                Console.WriteLine($"Loaded {UserStates.Count} entries");

                foreach (var user in UserStates)
                {
                    foreach (var position in user.Value.FuturesPositions)
                    {
                        var token = position.Pair.Split(':')[0].ToLowerInvariant();
                        if (!PositionsWatched.ContainsKey(token))
                        {
                            PositionsWatched[token] = new();
                        }
                        
                        PositionsWatched[token].Add(position);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Couldn't load user trade states: {ex}");
            }
        }

        public void Save()
        {
            File.WriteAllText(StateFile, JsonConvert.SerializeObject(UserStates));
        }

        public string OpenFuturesPosition(string source, string nick, PositionType type, string quoteToken, string actualToken,
            decimal collateral, decimal leverage)
        {
            quoteToken = quoteToken.ToLowerInvariant();
            actualToken = actualToken.ToLowerInvariant();
            
            if (quoteToken != "usdt")
            {
                return $"{nick}: Sorry";
            }
            
            if (leverage < 1 || leverage > 500)
            {
                return $"{nick}: Leverage must be between 1x and 500x. Syntax is like !trade long btc 100 25 for $100 worth of BTC at 25x leverage.";
            }
            var id = GetUserIdentifier(source, nick);
            if (!UserStates.ContainsKey(id))
            {
                return $"{nick}: You are not in the trade program";
            }
            
            var userState = UserStates[id];

            lock (userState)
            {
                var hasInToken = userState.GetBalance(quoteToken);

                if (collateral == -1)
                    collateral = hasInToken;

                if (collateral > hasInToken)
                {
                    return $"{nick}: Insufficient balance";
                }

                var entryPrice = (decimal) CryptoHandler.ConvertPrice(actualToken, quoteToken, 1);
                var collateralConverted = collateral / entryPrice;

                var pair = $"{actualToken}:{quoteToken}";

                var position =
                    FuturesPosition.CreatePosition(type, leverage, collateralConverted, entryPrice, pair, id);
                var liquidation = position.GetLiquidationPrice();

                userState.Balances[quoteToken] -= collateral;
                userState.FuturesPositions.Add(position);

                if (!PositionsWatched.ContainsKey(actualToken))
                {
                    PositionsWatched[actualToken] = new();
                }

                PositionsWatched[actualToken].Add(position);

                return $"{nick}: You opened a {pair.ToUpperInvariant()} position at ${entryPrice:0.00} per {actualToken.ToUpperInvariant()} that will liquidate at ${liquidation:0.00}";
            }
        }

        public string DoTrade(string source, string nick, string sellToken, string buyToken, decimal amount)
        {
            sellToken = sellToken.ToLowerInvariant();
            buyToken = buyToken.ToLowerInvariant();
            var id = GetUserIdentifier(source, nick);
            
            if (!UserStates.ContainsKey(id))
            {
                return $"{nick}: You are not in the trade program";
            }
            
            var userState = UserStates[id];

            lock (userState)
            {
                var hasInToken = userState.GetBalance(sellToken);

                if (amount == -1)
                    amount = hasInToken;

                var quotedOut = (decimal) CryptoHandler.ConvertPrice(sellToken, buyToken, (double) amount);

                if (quotedOut <= 0)
                {
                    return $"{nick}: Failed to fetch the price of coin \"{buyToken.ToUpperInvariant()}\". Try again if the coin exists";
                }

                if (amount <= 0 || amount > hasInToken)
                {
                    return $"{nick}: Insufficient funds. You have {hasInToken} {sellToken}";
                }
                
                var unitPrice = quotedOut / amount;
                bool priceInUsd = false;
                string nonUsdToken = sellToken.Contains("usd") ? buyToken : sellToken;
                if (sellToken.Contains("usd"))
                {
                    unitPrice = 1 / unitPrice;
                }

                if (sellToken.Contains("usd") || buyToken.Contains("usd"))
                {
                    priceInUsd = true;
                }

                var prevSellBal = userState.Balances[sellToken];
                userState.Balances[sellToken] -= amount;

                if (!userState.Balances.ContainsKey(buyToken))
                    userState.Balances[buyToken] = 0;

                userState.Balances[buyToken] += quotedOut;

                decimal pnl = -1m;

                if (buyToken == "usdt" || buyToken == "busd")
                {
                    if (sellToken != "usdt" && sellToken != "busd")
                    {
                        if (userState.CostBasis.ContainsKey(sellToken))
                        {
                            var cost = userState.CostBasis[sellToken] * (amount / prevSellBal);
                            pnl = quotedOut - cost;
                            userState.CumulativePnl += pnl;
                            userState.CostBasis[sellToken] -= cost;
                        }
                    }
                }
                else if (sellToken == "usdt" || sellToken == "busd")
                {
                    if (!userState.CostBasis.ContainsKey(buyToken))
                        userState.CostBasis[buyToken] = 0;

                    userState.CostBasis[buyToken] += amount;
                }

                return
                    $"{nick}: You traded {amount:##,#0.########} {sellToken} for {quotedOut:##,#0.########} {buyToken.ToUpperInvariant()} @ {(priceInUsd ? $"${unitPrice:0.00} per {nonUsdToken.ToUpperInvariant()}" : $"{unitPrice:0.0000} {buyToken.ToUpperInvariant()} per {sellToken.ToUpperInvariant()}")}{(pnl != -1 ? $" for a {(pnl > 0 ? "profit" : "loss")} of ${pnl:0.00}" : "")}";
            }
        }

        public List<TokenAmount> DumpBalances(string source, string nick)
        {
            var id = GetUserIdentifier(source, nick);
            var ret = new List<TokenAmount>();

            if (!UserStates.ContainsKey(id))
            {
                return ret;
            }

            ret = UserStates[id].DumpBalances();

            return ret;
        }

        public void InitIfNotIn(string source, string nick)
        {
            var id = GetUserIdentifier(source, nick);
            if (!UserStates.ContainsKey(id))
                InitUser(source, nick);
        }

        public Dictionary<string, TradeState> OldStates = new();

        public string InitUser(string source, string nick)
        {
            var id = GetUserIdentifier(source, nick);

            decimal pnl = 0m;

            if (UserStates.ContainsKey(id))
            {
                OldStates[id] = UserStates[id];

                var balances = DumpBalances(source, nick);
                var totalUsd = balances.Sum(b => b.UsdEquivalent);

                pnl = OldStates[id].CumulativePnl - totalUsd;

                UserStates[id] = new TradeState(id);
                UserStates[id].Balances["usdt"] = 1000;
                UserStates[id].CumulativePnl = pnl;

                return
                    $"{nick}: Being a wuss, you've added a ${totalUsd:0.00} loss (the value of your total assets) to your cumulative PNL counter (now ${pnl:0.00}). If you change your mind, do !trade undo for a LIMITED TIME to go back to your previous state.";
            }
            else
            {
                UserStates[id] = new TradeState(id);
                UserStates[id].Balances["usdt"] = 1000;
                UserStates[id].CumulativePnl = pnl;

                return null;
            }
            
        }

        public string GetUserIdentifier(string source, string nick)
        {
            Console.WriteLine($"source: \"{source}\"");
            
            if (source == "ezbake/#trade")
                source = "ezbake/#ezbake";
            Console.WriteLine($"source: \"{source}\"");

            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{source}/{nick}"));
        }
    }

    public class TokenAmount
    {
        public decimal Amount { get; set; }
        public string Token { get; set; }
        public decimal UsdEquivalent { get; set; }

        public override string ToString()
        {
            if (string.Equals(Token, "USDT", StringComparison.InvariantCultureIgnoreCase) ||
                string.Equals(Token, "USD", StringComparison.InvariantCultureIgnoreCase)
               )
            {
                return $"${Amount:0.00}";
            }

            return $"{Amount:##,#0.########} {Token.ToUpperInvariant()} (${UsdEquivalent:0.00})";
        }
    }

    public class FuturesPosition
    {
        public const decimal MaintenanceMargin = 0.001m;
        
        public string UserId { get; set; }
        public decimal Amount { get; set; }
        public decimal Collateral { get; set; }
        public decimal EntryPrice { get; set; }
        public PositionType Type { get; set; }
        
        public decimal Leverage { get; set; }
        
        public string Pair { get; set; }
        
        public DateTime Opened { get; set; }
        
        public decimal TakeProfit { get; set; }
        public decimal StopLoss { get; set; }

        private decimal _liqPrice = -1;

        public static FuturesPosition CreatePosition(PositionType type, decimal leverage, decimal collateral,
            decimal price, string pair, string user)
        {
            return new FuturesPosition()
            {
                Type = type,
                Leverage = leverage,
                Collateral = collateral,
                EntryPrice = price,
                Amount = leverage * collateral,
                Opened = DateTime.UtcNow,
                Pair = pair,
                UserId = user
            };
        }

        public decimal GetLiquidationPrice()
        {
            if (_liqPrice != -1)
                return _liqPrice;
            
            //Liquidation Value = Open Value + Maintenance Margin – Initial Margin

            //Liquidation Value = Open Value + Open Value x Maintenance Margin – (Open Value/Leverage)

            //Liquidation Price = Liquidation Value/(Contract Quantity x Contract Size)
            
            if (Type == PositionType.Short)
            {
                var modifiedCollateral = Collateral - (Amount * MaintenanceMargin);
                return _liqPrice = (EntryPrice + (EntryPrice * (modifiedCollateral / Amount)));
            }
            else
            {
                var modifiedCollateral = Collateral - (Amount * MaintenanceMargin);
                return _liqPrice = (EntryPrice - (EntryPrice * (modifiedCollateral / Amount)));
            }
        }

        public decimal GetValueAtPrice(decimal price)
        {
            //return (price / EntryPrice) * Amount - (Collateral);
            
            // current absolute value = price * Amount
            
            //(EntryP)
            // (EntryPrice - _liqPrice) * Amount = modifiedCollateral
            // EntryPrice - _liqPrice = modifiedCollateral / Amount
            // 
            
            if (Type == PositionType.Short)
            {
                return (Amount * (EntryPrice - price)) + (Collateral * EntryPrice);//  - (Amount * MaintenanceMargin * EntryPrice) // + (Collateral * EntryPrice);
            }
            else
            {
                return (Amount * (price - EntryPrice)) + (Collateral * EntryPrice); //+ (Collateral * EntryPrice);
            }
        }
    }

    public enum PositionType
    {
        Short,
        Long
    }

    public class TradeState
    {
        public string User { get; set; }
        public Dictionary<string, decimal> Balances { get; set; } = new();
        public Dictionary<string, decimal> CostBasis { get; set; } = new();
        public List<FuturesPosition> FuturesPositions { get; set; } = new();
        public decimal CumulativePnl { get; set; }

        public TradeState()
        {
        }

        public TradeState(string user)
        {
            User = user;
        }

        public decimal GetBalance(string token)
        {
            if (!Balances.ContainsKey(token))
                return 0;

            return Balances[token];
        }

        public List<TokenAmount> DumpBalances()
        {
            var ret = new List<TokenAmount>();
            foreach (var balance in Balances)
            {
                var amountObj = new TokenAmount()
                {
                    Amount = balance.Value,
                    Token = balance.Key.ToUpperInvariant()
                };

                if (amountObj.Token == "USDT" || amountObj.Token == "USD")
                {
                    amountObj.UsdEquivalent = amountObj.Amount;
                }
                else
                {
                    try
                    {
                        amountObj.UsdEquivalent = (decimal)CryptoHandler.ConvertPrice(amountObj.Token, "USDT", (double)amountObj.Amount);
                    }
                    catch
                    {
                        try
                        {
                            amountObj.UsdEquivalent = (decimal)CryptoHandler.ConvertPrice(amountObj.Token, "BUSD", (double)amountObj.Amount);

                        }
                        catch (Exception e)
                        {
                        }
                    }
                }
                
                ret.Add(amountObj);
            }

            return ret;
        }
    }
}