namespace TaxesUa.Api.Tests;

public sealed class FieldErrorsTests
{
    [Fact]
    public void An_empty_set_of_errors_is_null()
    {
        Assert.Null(new FieldErrors().OrNull());
    }

    [Fact]
    public void Setting_a_field_replaces_what_it_held_and_adding_keeps_it()
    {
        var errors = new FieldErrors();
        errors.Set("amount", ProblemCodes.NotPositive, "amount must be positive.");
        errors.Set("amount", ProblemCodes.AmountTooLarge, "amount is too large.");
        errors.Add("rate", ProblemCodes.OutOfRange, "rate is out of range.");
        errors.Add("rate", ProblemCodes.Required, "rate is required.");

        Assert.Equal(new() { ["amount"] = ["amount_too_large"], ["rate"] = ["out_of_range", "required"] }, errors.Codes());
        Assert.Equal(new() { ["amount"] = ["amount is too large."], ["rate"] = ["rate is out of range.", "rate is required."] }, errors.Messages());
        Assert.Same(errors, errors.OrNull());
    }

    [Fact]
    public void Merging_keeps_the_codes_under_the_prefix_and_can_rename_the_fields()
    {
        var inner = new FieldErrors();
        inner.Set("name", ProblemCodes.TooLong, "name is too long.");
        inner.Set("clientName", ProblemCodes.Required, "clientName is required.");
        var outer = new FieldErrors();

        outer.Merge("clients[2]", inner);
        outer.Merge("incomes[0]", inner, field => field == "clientName" ? "client" : field);

        Assert.Equal(
            new()
            {
                ["clients[2].name"] = ["too_long"],
                ["clients[2].clientName"] = ["required"],
                ["incomes[0].name"] = ["too_long"],
                ["incomes[0].client"] = ["required"],
            },
            outer.Codes());
    }

    [Fact]
    public void Each_field_has_as_many_sentences_as_codes_in_the_same_order()
    {
        var errors = new FieldErrors();
        errors.SetAll("file", [new Issue(ProblemCodes.DeclarationSchemaInvalid, "first"), new Issue(ProblemCodes.DeclarationSchemaInvalid, "second")]);

        Assert.Equal(["first", "second"], errors.Messages()["file"]);
        Assert.Equal(["declaration_schema_invalid", "declaration_schema_invalid"], errors.Codes()["file"]);
        Assert.Equal(["file"], errors.Fields);
    }
}
