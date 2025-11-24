using System;
using FluentValidation;

namespace OrderSystem.Api.Features.Orders;

public class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Customer ID is required.");
        RuleFor(x => x.TotalAmount).GreaterThan(0).WithMessage("Order amount must be greater than zero.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Order must contain items.");
    }
}
