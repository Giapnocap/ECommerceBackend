using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Application.Common
{
    public static class DomainRuleGuard
    {
        public static T AsBusiness<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (DomainRuleViolationException ex)
            {
                throw new BusinessException(ex.Code, ex.Message, ex);
            }
        }

        public static void AsBusiness(Action action)
        {
            try
            {
                action();
            }
            catch (DomainRuleViolationException ex)
            {
                throw new BusinessException(ex.Code, ex.Message, ex);
            }
        }

        public static T AsConflict<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (DomainRuleViolationException ex)
            {
                throw new ConflictException(ex.Code, ex.Message, ex);
            }
        }

        public static void AsConflict(Action action)
        {
            try
            {
                action();
            }
            catch (DomainRuleViolationException ex)
            {
                throw new ConflictException(ex.Code, ex.Message, ex);
            }
        }
    }
}