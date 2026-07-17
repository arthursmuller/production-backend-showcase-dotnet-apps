using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using StarterEntities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace EfCoreFilters
{
    public static class QueryableExtensions
    {
        public static bool CollateContains<T>(this IQueryable<T> _, string str1, string str2)
        {
            var collateOption = "utf8mb4_general_ci";
            return EF.Functions.Collate(str1, collateOption).Contains(EF.Functions.Collate(str2, collateOption));
        }
        public static IQueryable<T> ApplyFilter<T, TValue>(
            this IQueryable<T> query,
            TValue value,
            Expression<Func<T, TValue, bool>> filter)
        {
            if (value == null) return query;

            var shouldApplyFilter = value switch
            {
                Array arrayValue => arrayValue.Length > 0,
                string stringValue => !string.IsNullOrEmpty(stringValue),
                _ => true
            };

            if (!shouldApplyFilter) return query;

            var parameter = Expression.Parameter(typeof(T), "e");
            var constantValue = Expression.Constant(value, typeof(TValue));
            var filterBody = Expression.Invoke(filter, parameter, constantValue);
            var lambda = Expression.Lambda<Func<T, bool>>(filterBody, parameter);
            return query.Where(lambda);
        }

        public static IQueryable<T> ApplyFilter<T, T2>(
            this IQueryable<T> query,
            T2[] range,
            Expression<Func<T, T2>> selector) where T2 : struct, IComparable<T2>
        {
            if (range?.Length > 0)
            {
                T2? minValue = range[0];
                T2? maxValue = range.Length > 1 ? range[1] : null;

                if (minValue.HasValue)
                {
                    query = query.Where(BuildComparisonExpression(selector, minValue.Value, true));
                }
                if (maxValue.HasValue)
                {
                    query = query.Where(BuildComparisonExpression(selector, maxValue.Value, false));
                }
            }
            return query;
        }

        public static IQueryable<T> ApplyFilter<T, T2>(
            this IQueryable<T> query,
            T2?[] range,
            Expression<Func<T, T2?>> selector) where T2 : struct, IComparable<T2>
        {
            if (range?.Length > 0)
            {
                T2? minValue = range[0];
                T2? maxValue = range.Length > 1 ? range[1] : null;

                if (minValue.HasValue)
                {
                    query = query.Where(BuildNullableComparisonExpression(selector, minValue.Value, true));
                }
                if (maxValue.HasValue)
                {
                    query = query.Where(BuildNullableComparisonExpression(selector, maxValue.Value, false));
                }
            }
            return query;
        }

        private static Expression<Func<T, bool>> BuildComparisonExpression<T, T2>(
            Expression<Func<T, T2>> selector,
            T2 value,
            bool isGreaterThanOrEqual) where T2 : struct, IComparable<T2>
        {
            var parameter = selector.Parameters[0];
            var comparison = isGreaterThanOrEqual
                ? Expression.GreaterThanOrEqual(selector.Body, Expression.Constant(value))
                : Expression.LessThanOrEqual(selector.Body, Expression.Constant(value));

            return Expression.Lambda<Func<T, bool>>(comparison, parameter);
        }

        private static Expression<Func<T, bool>> BuildNullableComparisonExpression<T, T2>(
            Expression<Func<T, T2?>> selector,
            T2 value,
            bool isGreaterThanOrEqual) where T2 : struct, IComparable<T2>
        {
            var parameter = selector.Parameters[0];
            var hasValue = Expression.Property(selector.Body, "HasValue");
            var valueProperty = Expression.Property(selector.Body, "Value");

            var comparison = isGreaterThanOrEqual
                ? Expression.GreaterThanOrEqual(valueProperty, Expression.Constant(value))
                : Expression.LessThanOrEqual(valueProperty, Expression.Constant(value));

            var combined = Expression.AndAlso(hasValue, comparison);

            return Expression.Lambda<Func<T, bool>>(combined, parameter);
        }

        public static IQueryable<T> IncludeIfNotNull<T, TValue>(
           this IQueryable<T> query,
           Expression<Func<T, object>> include,
           TValue value)
           where T : class
        {
            return value != null ? query.Include(include) : query;
        }

        public static async Task<(IEnumerable<T>, int)> Filter<T>(
               this IQueryable<T> query,
               CommomFilter criteria,
               Func<DateTime, DateTime?, IQueryable<T>, IQueryable<T>> filterDateRange = null,
               Func<string, IOrderedQueryable<T>, IOrderedQueryable<T>> sortable = null,
               Func<string, IQueryable<T>, IQueryable<T>> overrideSortable = null, 
               CancellationToken cancellationToken = default) where T : BaseEntity
        {
            if (criteria.DateRange?.Length > 0 && criteria.DateRange is not null && filterDateRange is not null)
            {
                if (criteria.DateRange[0] is not null)
                {
                    DateTime startDate = (DateTime)criteria.DateRange[0];
                    DateTime? endDate = criteria.DateRange?.Length > 1 ? criteria.DateRange[1] : null;
                    query = filterDateRange(startDate, endDate, query);
                }
            }

            IOrderedQueryable<T> newQuery = criteria.SortBy switch
            {
                "date_asc" => query.OrderBy(s => s.CreatedDate),
                "date_desc" => query.OrderByDescending(s => s.CreatedDate),
                _ => null,
            };

            if (newQuery is not null)
            {
                if (sortable is not null)
                    sortable(criteria.SortBy, newQuery);

                query = newQuery;
            }

            query = overrideSortable is not null ? overrideSortable(criteria.SortBy, query) : query;
            
            return await query.Paginate(criteria, cancellationToken);
        }

        public static async Task<(IEnumerable<TSelectRes>, int)> Filter<T, TSelectRes>(
              this IQueryable<T> query,
              CommomFilter criteria,
              Func<DateTime, DateTime?, IQueryable<T>, IQueryable<T>> filterDateRange = null,
              Func<string, IOrderedQueryable<T>, IOrderedQueryable<T>> sortable = null,
              Func<T, TSelectRes> select = null
        ) where T : BaseEntity
        {
            if (criteria.DateRange?.Length > 0 && criteria.DateRange is not null && filterDateRange is not null)
            {
                if (criteria.DateRange[0] is not null)
                {
                    DateTime startDate = (DateTime) criteria.DateRange[0];
                    DateTime? endDate = criteria.DateRange?.Length > 1 ? criteria.DateRange[1] : null;
                    query = filterDateRange(startDate, endDate, query);
                }
            }

            query = criteria.SortBy switch
            {
                "date_asc" => query.OrderBy(s => s.CreatedDate),
                "date_desc" => query.OrderByDescending(s => s.CreatedDate),
                _ => query,
            };

            if (sortable is not null)
                sortable(criteria.SortBy, (IOrderedQueryable<T>)query);

            return await query.Paginate(criteria, select);
        }


        public static async Task<(IEnumerable<TSelectRes>, int)> Paginate<T, TSelectRes>(
           this IQueryable<T> query,
           CommomFilter criteria,
           Func<T,  TSelectRes> selector
        )
        {
            var pageNumber = criteria.PageNumber ?? 1;
            var pageSize = criteria.PageSize ?? 10;
            var data = await query
               .Select(selector)
               .AsQueryable()
               .Skip((pageNumber - 1) * pageSize)
               .Take(pageSize)
               .ToListAsync();

            var totalRecords = await query.CountAsync();

            return (data, totalRecords);
        }

        public static async Task<(IEnumerable<T>, int)> Paginate<T>(
            this IQueryable<T> query,
            CommomFilter criteria,
            CancellationToken cancellationToken)
        {
            var pageNumber = criteria.PageNumber ?? 1;
            var pageSize = criteria.PageSize ?? 10;
            var data = await query
               .Skip((pageNumber - 1) * pageSize)
               .Take(pageSize)
               .ToListAsync(cancellationToken);

            var totalRecords = await query.CountAsync(cancellationToken);

            return (data, totalRecords);
        }

        public static async Task<(IEnumerable<T>, int)> Paginate<T>(
            this IQueryable<T> query,
            CommomFilter criteria)
        {
            var pageNumber = criteria.PageNumber ?? 1;
            var pageSize = criteria.PageSize ?? 10;
            var data = await query
               .Skip((pageNumber - 1) * pageSize)
               .Take(pageSize)
               .ToListAsync();

            var totalRecords = await query.CountAsync();

            return (data, totalRecords);
        }
    }
    public static class TransactionExtensions
    {
        public static async Task<IDbContextTransaction> BeginTransactionAsync(this DbContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.Database.CurrentTransaction != null)
                throw new InvalidOperationException("An active transaction already exists.");

            return await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        }

        public static async Task CommitTransactionAsync(this DbContext context, IDbContextTransaction transaction)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            if (transaction != context.Database.CurrentTransaction)
                throw new InvalidOperationException($"Transaction {transaction.TransactionId} is not the current transaction.");

            try
            {
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await RollbackTransactionAsync(context, transaction);
                throw;
            }
        }

        public static async Task<T> Transaction<T>(this DbContext context, Func<Func<Task>, Task<T>> action)
        {
            using (var transaction = await context.BeginTransactionAsync())
            {
                try
                {
                    var result = await action(() => context.CommitTransactionAsync(transaction));
                    return result;
                }
                catch (Exception)
                {
                    await RollbackTransactionAsync(context, transaction);
                    throw;
                }
            }
        }

        public static async Task Transaction(this DbContext context, Func<Func<Task>, Task> action)
        {
            using (var transaction = await context.BeginTransactionAsync())
            {
                try
                {
                    await action(() => context.CommitTransactionAsync(transaction));
                }
                catch (Exception)
                {
                    await RollbackTransactionAsync(context, transaction);
                    throw;
                }
            }
        }

        private static async Task RollbackTransactionAsync(DbContext context, IDbContextTransaction transaction)
        {
            try
            {
                await transaction.RollbackAsync();
            }
            finally
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
