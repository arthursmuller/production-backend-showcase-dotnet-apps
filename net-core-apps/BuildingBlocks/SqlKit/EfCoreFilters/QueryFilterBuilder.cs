using System;
using System.Linq;
using System.Linq.Expressions;
using StarterEntities;

namespace EfCoreFilters
{
    public class DbFilters<T> where T : BaseEntity
    {
        private IQueryable<T> query;

        public DbFilters(IQueryable<T> initialQuery)
        {
            query = initialQuery;
        }

        public DbFilters<T> New<TValue>(
            TValue param,
            Expression<Func<T, TValue, bool>> predicate) where TValue : notnull
        {
            query = query.ApplyFilter(param, predicate);
            return this;
        }

        public DbFilters<T> New(
            decimal?[] range,
            Expression<Func<T, decimal?>> selector)
        {
            query = query.ApplyFilter(range, selector);
            return this;
        }

        public DbFilters<T> New(
           decimal[] range,
           Expression<Func<T, decimal>> selector)
        {
            query = query.ApplyFilter(range, selector);
            return this;
        }


        public DbFilters<T> New<TValue>(
            TValue param,
            Expression<Func<T, TValue, bool>> predicate,
            params Expression<Func<T, object>>[] includes) where TValue : notnull
        {
            query = query.ApplyFilter(param, predicate);

            foreach (var include in includes)
            {
                query = query.IncludeIfNotNull(include, param);

            }
            return this;
        }

        public IQueryable<T> Build() => query;
    }
}
