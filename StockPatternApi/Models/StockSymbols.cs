namespace StockPatternApi.Models
{
    public static class StockSymbols
    {
        /// <summary>
        /// Shared blue-chip / mega-cap / liquid large-cap universe (Dow + heavy Nasdaq + staples/financials/industrials).
        /// Single source of truth for StockPatternAPIBot CLI and API getStockSetups.
        /// </summary>
        public static readonly string[] Tickers =
        [
            // Dow 30
            "AAPL","AMGN","AMZN","AXP","BA","CAT","CRM","CSCO","CVX","DIS","DOW","GS","HD","HON","IBM",
            "INTC","JNJ","JPM","KO","MCD","MMM","MRK","MSFT","NKE","NVDA","PG","SHW","TRV","UNH","V","VZ","WMT",
            // Mega / large tech & growth
            "GOOGL","GOOG","META","TSLA","AVGO","ORCL","ADBE","AMD","QCOM","TXN","INTU","NOW","PANW","CRWD","SNPS",
            "CDNS","KLAC","LRCX","AMAT","MU","ADI","NXPI","MRVL","FTNT","DDOG","NET","SNOW","PLTR","SHOP","SQ",
            "UBER","ABNB","COIN","MSTR","APP","ARM","SMCI",
            // Financials
            "BAC","WFC","C","MS","BLK","SCHW","BX","KKR","AIG","MET","PRU","USB","PNC","TFC","COF","AXP",
            "CB","MMC","PGR","ALL","AFL","BK","STT","ICE","CME","SPGI","MCO",
            // Health care
            "LLY","ABBV","MRNA","PFE","BMY","AMGN","GILD","BIIB","REGN","VRTX","ISRG","SYK","BSX","MDT","ZBH",
            "CI","ELV","CVS","HUM","CNC","HCA","IQV","TMO","DHR","A","BDX","EW","IDXX","DXCM","PODD",
            // Consumer
            "COST","TGT","HD","LOW","NKE","SBUX","MCD","CMG","YUM","BKNG","MAR","HLT","CCL","RCL","NCLH",
            "NFLX","CMCSA","CHTR","T","TMUS","DIS","EA","TTWO","RBLX","PEP","KO","PM","MO","MDLZ","CL","KMB",
            "PG","EL","LULU","ROST","TJX","ORLY","AZO","DG","DLTR",
            // Industrials / energy / materials
            "GE","HON","UNP","UPS","FDX","CAT","DE","EMR","ETN","ITW","PH","ROK","CARR","OTIS","TT","WM","RSG",
            "XOM","CVX","COP","EOG","SLB","HAL","OXY","MPC","VLO","PSX","WMB","KMI","LIN","APD","ECL","SHW","FCX","NEM",
            // Utilities / REITs / other blue chips
            "NEE","DUK","SO","D","AEP","SRE","EXC","PEG","ED","XEL","AMT","PLD","CCI","EQIX","SPG","O","WELL","DLR",
            // Extra liquid names often treated as blue-chip adjacent
            "BA","LMT","RTX","NOC","GD","LHX","TDG","GEV","ANET","DELL","HPQ","CSCO","ADP","PAYX","FI","FIS","GPN",
            "MA","V","PYPL","XYZ","HOOD","SOFI","NU"
        ];

        public static string[] DistinctOrdered() =>
            Tickers.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToArray();
    }
}
