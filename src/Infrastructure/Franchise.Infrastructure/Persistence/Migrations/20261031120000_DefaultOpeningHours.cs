using Franchise.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Franchise.Infrastructure.Persistence.Migrations
{
    /// <summary>A restaurant without opening hours used to count as always
    /// open; it now counts as never open. The ones already in service get the
    /// default hours of a new restaurant (every day 11:00-23:00) rather than
    /// closing overnight; their franchisee adjusts them in the portal.</summary>
    [DbContext(typeof(FranchiseDbContext))]
    [Migration("20261031120000_DefaultOpeningHours")]
    public partial class DefaultOpeningHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """UPDATE restaurants SET "OpeningHours" = '[{"day":0,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false},{"day":1,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false},{"day":2,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false},{"day":3,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false},{"day":4,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false},{"day":5,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false},{"day":6,"opens":"11:00:00","closes":"23:00:00","runsPastMidnight":false}]'::jsonb WHERE "OpeningHours" = '[]'::jsonb;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Hours given since cannot be told apart from the defaults: kept.
        }
    }
}
