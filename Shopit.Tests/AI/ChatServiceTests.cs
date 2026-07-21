using System.Text.Json.Nodes;
using FluentAssertions;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.AI;

public class ChatServiceTests
{
    private static readonly string[] AllToolNames =
    {
        "get_dashboard_summary",
        "search_products",
        "get_low_stock_products",
        "get_order",
        "get_customer_orders",
        "get_product",
        "answer_feature_question"
    };

    [Fact]
    public void GetAllowedToolNames_CustomerRole_IncludesAnswerFeatureQuestion()
    {
        // SCRUM-166: feature Q&A must be usable by any authenticated role, not
        // just Admin, so it belongs to the customer allow-list.
        var allowed = ChatService.GetAllowedToolNames("Customer", AllToolNames);

        allowed.Should().Contain("answer_feature_question");
    }

    [Fact]
    public void GetAllowedToolNames_AdminRole_IncludesAnswerFeatureQuestion()
    {
        var allowed = ChatService.GetAllowedToolNames("Admin", AllToolNames);

        allowed.Should().Contain("answer_feature_question");
    }

    [Fact]
    public void GetAllowedToolNames_AdminRole_ReturnsAllTools()
    {
        var allowed = ChatService.GetAllowedToolNames("Admin", AllToolNames);

        allowed.Should().BeEquivalentTo(AllToolNames);
    }

    [Fact]
    public void GetAllowedToolNames_CustomerRole_ExcludesDashboardSummary()
    {
        var allowed = ChatService.GetAllowedToolNames("Customer", AllToolNames);

        allowed.Should().NotContain("get_dashboard_summary");
    }

    [Fact]
    public void GetAllowedToolNames_CustomerRole_ExcludesLowStockProducts()
    {
        var allowed = ChatService.GetAllowedToolNames("Customer", AllToolNames);

        allowed.Should().NotContain("get_low_stock_products");
    }

    [Fact]
    public void GetAllowedToolNames_CustomerRole_ExcludesGetOrder()
    {
        // get_order hardcodes isAdmin: true internally — it must never be
        // reachable by a non-admin caller, regardless of model behavior.
        var allowed = ChatService.GetAllowedToolNames("Customer", AllToolNames);

        allowed.Should().NotContain("get_order");
    }

    [Fact]
    public void GetAllowedToolNames_CustomerRole_IncludesSearchProducts()
    {
        var allowed = ChatService.GetAllowedToolNames("Customer", AllToolNames);

        allowed.Should().Contain("search_products");
    }

    [Fact]
    public void GetAllowedToolNames_CustomerRole_IncludesGetCustomerOrders()
    {
        var allowed = ChatService.GetAllowedToolNames("Customer", AllToolNames);

        allowed.Should().Contain("get_customer_orders");
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("CUSTOMER")]
    [InlineData("Customer")]
    public void GetAllowedToolNames_RoleComparisonIsCaseInsensitive(string role)
    {
        var allowed = ChatService.GetAllowedToolNames(role, AllToolNames);

        allowed.Should().NotContain("get_dashboard_summary");
        allowed.Should().Contain("search_products");
    }

    [Fact]
    public void GetAllowedToolNames_UnknownRole_DefaultsToCustomerAllowList()
    {
        // Any role that isn't explicitly Admin is treated as the restrictive
        // default — there is no implicit trust for unrecognized role strings.
        var allowed = ChatService.GetAllowedToolNames("SomeUnknownRole", AllToolNames);

        allowed.Should().NotContain("get_dashboard_summary");
        allowed.Should().NotContain("get_low_stock_products");
        allowed.Should().NotContain("get_order");
    }

    [Fact]
    public void ApplyIdentityInjection_GetCustomerOrders_OverridesModelSuppliedUserId()
    {
        // The model asked for a different user's orders (userId 999) — the
        // caller is actually user 5. The model's value must never survive.
        var modelArgs = new JsonObject
        {
            ["userId"] = 999,
            ["page"] = 1,
            ["pageSize"] = 10
        };

        var result = ChatService.ApplyIdentityInjection("get_customer_orders", modelArgs, callerUserId: 5, callerRole: "Customer");

        result["userId"]!.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public void ApplyIdentityInjection_GetCustomerOrders_PreservesNonIdentityArguments()
    {
        var modelArgs = new JsonObject
        {
            ["userId"] = 999,
            ["page"] = 2,
            ["pageSize"] = 25
        };

        var result = ChatService.ApplyIdentityInjection("get_customer_orders", modelArgs, callerUserId: 5, callerRole: "Customer");

        result["page"]!.GetValue<int>().Should().Be(2);
        result["pageSize"]!.GetValue<int>().Should().Be(25);
    }

    [Fact]
    public void ApplyIdentityInjection_GetCustomerOrders_NoModelArgsSupplied_StillInjectsCallerUserId()
    {
        // Defends against a model that omits userId entirely, hoping the tool
        // falls back to some default — the caller's own id is injected regardless.
        var result = ChatService.ApplyIdentityInjection("get_customer_orders", modelSuppliedArgs: null, callerUserId: 7, callerRole: "Customer");

        result["userId"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public void ApplyIdentityInjection_NonIdentityTool_LeavesArgumentsUnchanged()
    {
        // search_products has no notion of "whose data" — it must not be
        // touched by identity injection at all.
        var modelArgs = new JsonObject
        {
            ["search"] = "phone",
            ["pageNumber"] = 1
        };

        var result = ChatService.ApplyIdentityInjection("search_products", modelArgs, callerUserId: 5, callerRole: "Customer");

        result["search"]!.GetValue<string>().Should().Be("phone");
        result.ContainsKey("userId").Should().BeFalse();
    }

    [Theory]
    [InlineData("GET_CUSTOMER_ORDERS")]
    [InlineData("get_Customer_Orders")]
    public void ApplyIdentityInjection_ToolNameComparisonIsCaseInsensitive(string toolName)
    {
        var modelArgs = new JsonObject { ["userId"] = 999 };

        var result = ChatService.ApplyIdentityInjection(toolName, modelArgs, callerUserId: 5, callerRole: "Customer");

        result["userId"]!.GetValue<int>().Should().Be(5);
    }
}
