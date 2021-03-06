using System.Threading.Tasks;
using Npgsql;
using Dapper;
using System.Linq;
using System;
using System.Data;
using E247.Fun;
using System.Collections.Generic;

namespace TardisBank.Api
{
    public static class Db
    {
        public static Task<Login> InsertLogin(string connectionString, Login login)
            => WithConnection(connectionString, async conn =>
            {
                var result = await conn.QueryAsync<Login>(
                    "INSERT INTO login (email, password_hash, verified) " + 
                    "VALUES (@Email, @PasswordHash, FALSE) " + 
                    "RETURNING login_id as LoginId, email as Email, password_hash as PasswordHash",
                    login);

                return result.Single();
            });

        public static Task UpdateLoginPassword(string connectionString, int loginId, string newPasswordHash)
            => WithConnection(connectionString, async conn =>
            {
                var result = await conn.QueryAsync<Login>(
                    "UPDATE login SET password_hash = @NewPasswordHash WHERE login_id = @LoginId",
                    new { LoginId = loginId, NewPasswordHash = newPasswordHash });
            });

        public static Task UpdateLoginSetVerified(string connectionString, int loginId)
            => WithConnection(connectionString, async conn =>
            {
                var result = await conn.QueryAsync<Login>(
                    "UPDATE login SET verified = TRUE WHERE login_id = @LoginId",
                    new { LoginId = loginId });
            });

        public static Task<Login> LoginById(string connectionString, int loginId)
            => WithConnection<Login>(connectionString, 
            async conn =>
            {
                var result = await conn.QueryAsync<Login>(
                    "SELECT login_id as LoginId, email as Email, password_hash as PasswordHash " + 
                    "FROM login WHERE login_id = @LoginId",
                    new { LoginId = loginId });

                if(result.Any())
                {
                    return result.Single();
                }
                // throw rather than returning Maybe<Login>. Any id based lookup
                // should succeed, so this a server fault and should be thrown.
                throw new ApplicationException("LoginId not found in DB.");
            });

        public static Task<Maybe<Login>> LoginByEmail(string connectionString, string email)
            => WithConnection<Maybe<Login>>(connectionString, 
            async conn =>
            {
                var result = await conn.QueryAsync<Login>(
                    "SELECT login_id as LoginId, email as Email, password_hash as PasswordHash " + 
                    "FROM login WHERE email = @Email AND verified",
                    new { Email = email });

                if(result.Any())
                {
                    return result.Single();
                }
                return Maybe<Login>.Empty();
            });

        public static Task DeleteLogin(string connectionString, Login login)
            => WithConnection(connectionString, async conn =>
            {
                await conn.ExecuteAsync("DELETE FROM login WHERE login_id = @LoginId", login);
            });

        public static Task<Account> InsertAccount(string connectionString, Account account)
            => WithConnection<Account>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Account>(
                    "INSERT INTO account (login_id, account_name) " + 
                    "VALUES (@LoginId, @AccountName) " + 
                    "RETURNING account_id as AccountId, login_id as LoginId, account_name as AccountName",
                    account);

                return result.Single();
            });

        public static Task<Maybe<Account>> AccountById(string connectionString, int accountId)
            => WithConnection<Maybe<Account>>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Account>(
                    "SELECT account_id as AccountId, login_id as LoginId, account_name as AccountName " +
                    "FROM account " +
                    "WHERE account_id = @Id", 
                    new { Id = accountId });

                return result.SingleOrDefault() ?? Maybe<Account>.Empty();
            });

        public static Task<IEnumerable<Account>> AccountByLogin(string connectionString, Login login)
            => WithConnection<IEnumerable<Account>>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Account>(
                    "SELECT account_id as AccountId, login_id as LoginId, account_name as AccountName " +
                    "FROM account " +
                    "WHERE login_id = @LoginId", 
                    login);

                return result;
            });

        public static Task DeleteAccount(string connectionString, Account account)
            => WithConnection(connectionString, async conn => 
            {
                await conn.ExecuteAsync("DELETE FROM account WHERE account_id = @AccountId", account);
            });

        public static Task<Transaction> InsertTransaction(string connectionString, Transaction transaction)
            => WithConnection<Transaction>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Transaction>(
                    @"INSERT INTO trans (account_id, trans_date, amount, balance)
                    VALUES (@AccountId, @TransactionDate, @Amount, @Balance)
                    RETURNING 
                    trans_id as TransactionId,
                    account_id as AccountId,
                    trans_date as TransactionDate,
                    amount as Amount,
                    balance as Balance",
                    transaction);

                return result.Single();
            });

        public static Task<IEnumerable<Transaction>> TransactionsByAccount(string connectionString, Account account)
            => WithConnection<IEnumerable<Transaction>>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Transaction>(
                    @"SELECT
                    trans_id as TransactionId,
                    account_id as AccountId,
                    trans_date as TransactionDate,
                    amount as Amount,
                    balance as Balance
                    FROM trans
                    WHERE account_id = @AccountId
                    ORDER BY trans_date DESC
                    LIMIT 100", 
                    account);
                return result;
            });

        public static Task<Schedule> InsertSchedule(string connectionString, Schedule schedule)
            => WithConnection<Schedule>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Schedule>(
                    @"INSERT INTO schedule (account_id, time_period, next_run, amount)
                    VALUES (@AccountId, @TimePeriod, @NextRun, @Amount)
                    RETURNING
                    schedule_id as ScheduleId,
                    account_id as AccountId,
                    time_period as TimePeriod,
                    next_run as NextRun,
                    amount as Amount", 
                    new
                    {
                        schedule.ScheduleId,
                        schedule.AccountId,
                        TimePeriod = schedule.TimePeriod.ToString(),
                        schedule.NextRun,
                        schedule.Amount
                    });
                return result.Single();
            });

        public static Task<IEnumerable<Schedule>> ScheduleByAccount(string connectionString, Account account)
            => WithConnection<IEnumerable<Schedule>>(connectionString, async conn => 
            {
                var result = await conn.QueryAsync<Schedule>(
                    @"SELECT
                    schedule_id as ScheduleId,
                    account_id as AccountId,
                    time_period as TimePeriod,
                    next_run as NextRun,
                    amount as Amount
                    FROM schedule
                    WHERE account_id = @AccountId", 
                    account);
                return result;
            });

        public static Task DeleteSchedule(string connectionString, Schedule schedule)
            => WithConnection(connectionString, async conn => 
            {
                await conn.ExecuteAsync(
                    "DELETE FROM schedule WHERE schedule_id = @ScheduleId AND account_id = @AccountId", 
                    schedule);
            });

        private async static Task<T> WithConnection<T>(
            string connectionString, 
            Func<IDbConnection, Task<T>> connectionFunction)
        {
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                return await connectionFunction(conn);
            }
        }

        private async static Task WithConnection(
            string connectionString, 
            Func<IDbConnection, Task> connectionFunction)
        {
            using (var conn = new NpgsqlConnection(connectionString))
            {
                conn.Open();

                await connectionFunction(conn);
            }
        }
    }
}
