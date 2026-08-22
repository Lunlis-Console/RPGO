using LostAndDivine.Shared;
using Xunit;

namespace LostAndDivine.Tests;

/// <summary>
/// Проверяет, что UnsubscribeAll (через SubscriptionMatcher) корректно находит не только
/// method-group обработчики, но и лямбда-подписки, захватывающие this — иначе singleton
/// GameClient удерживал бы экран в памяти (утечка при logout→login, P0-2).
/// </summary>
public class SubscriptionMatcherTests
{
    private sealed class Subscriber
    {
        public int Counter;

        public Action MethodGroupHandler => OnEvent;

        private void OnEvent() => Counter++;

        public Action LambdaCapturingThis() => () => Counter++;

        public Action LambdaCapturingLocal()
        {
            var local = 42;
            return () => local.ToString();
        }

        public static Action StaticLambda() => () => { };
    }

    [Fact]
    public void Method_group_handler_is_owned_by_target()
    {
        var s = new Subscriber();
        Assert.True(SubscriptionMatcher.IsOwnedBy(s.MethodGroupHandler, s));
    }

    [Fact]
    public void Lambda_capturing_this_is_owned_by_target()
    {
        var s = new Subscriber();
        Assert.True(SubscriptionMatcher.IsOwnedBy(s.LambdaCapturingThis(), s));
    }

    [Fact]
    public void Lambda_capturing_local_is_not_owned_by_unrelated_target()
    {
        var s = new Subscriber();
        var other = new Subscriber();
        Assert.False(SubscriptionMatcher.IsOwnedBy(s.LambdaCapturingLocal(), other));
    }

    [Fact]
    public void Static_lambda_is_not_owned_by_any_target()
    {
        var s = new Subscriber();
        Assert.False(SubscriptionMatcher.IsOwnedBy(Subscriber.StaticLambda(), s));
    }

    [Fact]
    public void Null_arguments_are_not_owned()
    {
        var s = new Subscriber();
        Assert.False(SubscriptionMatcher.IsOwnedBy(null, s));
        Assert.False(SubscriptionMatcher.IsOwnedBy(s.MethodGroupHandler, null));
    }
}
