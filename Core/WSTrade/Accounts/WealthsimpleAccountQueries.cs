namespace Core.WSTrade.Accounts;

// Minimal selections from the operator's September 10, 2026 browser capture.
// Only these fixed query documents can reach the authenticated resource endpoint.
internal static class WealthsimpleAccountQueries
{
    // Display-only subset of FetchAccountGraphData in the September 16 capture.
    // Use the broker's simple return and reference, not arithmetic on sampled values/deposits.
    internal const string DailyPerformance = """
        query TraderVITfsaDailyPerformance($id: ID!, $currency: Currency!, $timeRange: GraphTimeRange!, $marketSession: GraphMarketSession!, $includeSimpleReturns: Boolean = false) {
          account(id: $id) { id financials { currentCombined(currency: $currency) {
            graphData(timeRange: $timeRange, marketSession: $marketSession) {
              previousClose { dateTime }
              data {
                dateTime
                simpleReturns @include(if: $includeSimpleReturns) {
                  amount { amount currency }
                  rate
                  referenceDateTime
                }
              }
            }
          } } }
        }
        """;

    // Minimal read from the September 16 account-activity capture. Keep previous close
    // separate from timed points; neither a sampled maximum nor a later point is an opening.
    internal const string DayValues = """
        query TraderVITfsaDayValues($id: ID!, $currency: Currency!, $timeRange: GraphTimeRange!, $marketSession: GraphMarketSession!) {
          account(id: $id) { id financials { currentCombined(currency: $currency) {
            graphData(timeRange: $timeRange, marketSession: $marketSession) {
              previousClose { dateTime netLiquidationValue { amount currency } }
              data {
                dateTime
                netLiquidationValue { amount currency }
                netDeposits { amount currency }
              }
            }
          } } }
        }
        """;

    internal const string Accounts = """
        query TraderVITfsaAccounts($identityId: ID!, $cursor: String) {
          identity(id: $identityId) { id accounts(first: 25, after: $cursor) {
            edges { node { id type branch unifiedAccountType isOpen closedAt archivedAt nickname currency } }
            pageInfo { hasNextPage endCursor }
          } }
        }
        """;
    internal const string Financials = """
        query TraderVITfsaFinancials($ids: [String!]!, $currency: Currency) {
          accounts(ids: $ids) { id financials { currentCombined(currency: $currency) {
            netLiquidationValueV2 { amount currency }
            netDepositsV2 { amount currency }
            simpleReturns { amount { amount currency } rate referenceDate asOf }
          } } }
        }
        """;
    internal const string Balances = """
        query TraderVITfsaBalances($accountCanonicalId: ID!, $currency: Currency!) {
          account(id: $accountCanonicalId) { id financials { current { tradingBalanceViewV2 {
            cash(currency: $currency) { quantity currency }
            buyingPower(currency: $currency) { quantity currency }
          } } } }
        }
        """;
    internal const string Positions = """
        query TraderVITfsaPositions($identityId: ID!, $accountIds: [ID!], $currency: Currency!, $cursor: String) {
          identity(id: $identityId) { id financials(filter: {accounts: $accountIds}) {
            current(currency: $currency) { positions(first: 50, after: $cursor, aggregated: true, filter: {includeStrategies: true}) {
              edges { node {
                id quantity accounts { id } security { id securityType stock { symbol name } }
                totalValue { amount currency } bookValue { amount currency } unrealizedReturns { amount currency }
              } }
              pageInfo { hasNextPage endCursor } totalCount status
            } }
          } }
        }
        """;
}
