namespace IdiotProof.Backend.Enums
{
    /// <summary>
    /// Specifies the exchange for order routing.
    /// </summary>
    public enum ContractExchange
    {
        /// <summary>
        /// SMART routing - IB's intelligent order routing system.
        /// Automatically routes to the best available exchange.
        /// </summary>
        Smart,

        /// <summary>
        /// OTC Pink Sheets - for microcap and penny stocks.
        /// Required for many sub-$1 and OTC-listed securities.
        /// </summary>
        Pink
    }
}


