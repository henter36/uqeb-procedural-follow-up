using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Uqeb.Api.Migrations
{
    public partial class AddInternalTrackingNumberSequence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @year NVARCHAR(4) = CAST(YEAR(GETUTCDATE()) AS NVARCHAR(4));
DECLARE @prefix NVARCHAR(16) = N'UQEB-' + @year + N'-';
DECLARE @maxSeq INT = 0;

SELECT @maxSeq = ISNULL(MAX(TRY_CAST(RIGHT(InternalTrackingNumber, 5) AS INT)), 0)
FROM [Transactions]
WHERE [InternalTrackingNumber] LIKE @prefix + N'%';

DECLARE @start BIGINT = @maxSeq + 1;
IF @start < 1 SET @start = 1;

DECLARE @sql NVARCHAR(MAX) = N'
CREATE SEQUENCE [TransactionTrackingSequence]
    AS BIGINT
    START WITH ' + CAST(@start AS NVARCHAR(20)) + N'
    INCREMENT BY 1
    MINVALUE 1
    NO CACHE;';

EXEC sp_executesql @sql;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS [TransactionTrackingSequence];");
        }
    }
}
