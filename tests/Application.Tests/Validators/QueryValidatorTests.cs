using FluentAssertions;
using FluentValidation.TestHelper;
using MERSEL.Services.GibUserList.Application.Models;
using MERSEL.Services.GibUserList.Application.Queries;

namespace MERSEL.Services.GibUserList.Application.Tests.Validators;

public class GetGibUserByIdentifierQueryValidatorTests
{
    private readonly GetGibUserByIdentifierQueryValidator _validator = new();

    [Theory]
    [InlineData("1234567890")]   // 10 haneli VKN
    [InlineData("12345678901")]  // 11 haneli TCKN
    public void Validate_WithValidIdentifier_ShouldPass(string identifier)
    {
        var query = new GetGibUserByIdentifierQuery(identifier, GibUserDocumentType.EInvoice);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]            // Boş
    [InlineData("123")]         // Çok kısa
    [InlineData("123456789")]   // 9 hane
    [InlineData("123456789012")] // 12 hane
    [InlineData("abcdefghij")]  // Harf içeriyor
    [InlineData("12345 6789")]  // Boşluk içeriyor
    public void Validate_WithInvalidIdentifier_ShouldFail(string identifier)
    {
        var query = new GetGibUserByIdentifierQuery(identifier, GibUserDocumentType.EInvoice);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Identifier);
    }

    [Fact]
    public void Validate_WithInvalidDocumentType_ShouldFail()
    {
        var query = new GetGibUserByIdentifierQuery("1234567890", (GibUserDocumentType)99);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.DocumentType);
    }
}

public class GetGibUsersByIdentifiersQueryValidatorTests
{
    private readonly GetGibUsersByIdentifiersQueryValidator _validator = new();

    [Fact]
    public void Validate_WithValidIdentifiers_ShouldPass()
    {
        var query = new GetGibUsersByIdentifiersQuery(
            ["1234567890", "9876543210"],
            GibUserDocumentType.EInvoice);

        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptyList_ShouldFail()
    {
        var query = new GetGibUsersByIdentifiersQuery(
            [],
            GibUserDocumentType.EInvoice);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Identifiers);
    }

    [Fact]
    public void Validate_WithMoreThan100Identifiers_ShouldFail()
    {
        var identifiers = Enumerable.Range(0, 101)
            .Select(i => $"{i:D10}")
            .ToList();

        var query = new GetGibUsersByIdentifiersQuery(
            identifiers,
            GibUserDocumentType.EInvoice);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Identifiers);
    }

    [Fact]
    public void Validate_With100Identifiers_ShouldPass()
    {
        var identifiers = Enumerable.Range(0, 100)
            .Select(i => $"{i:D10}")
            .ToList();

        var query = new GetGibUsersByIdentifiersQuery(
            identifiers,
            GibUserDocumentType.EInvoice);

        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Identifiers);
    }

    [Fact]
    public void Validate_WithInvalidIdentifierInList_ShouldFail()
    {
        var query = new GetGibUsersByIdentifiersQuery(
            ["1234567890", "abc", "9876543210"],
            GibUserDocumentType.EInvoice);

        var result = _validator.TestValidate(query);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_WithEmptyIdentifierInList_ShouldFail()
    {
        var query = new GetGibUsersByIdentifiersQuery(
            ["1234567890", ""],
            GibUserDocumentType.EInvoice);

        var result = _validator.TestValidate(query);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Validate_WithInvalidDocumentType_ShouldFail()
    {
        var query = new GetGibUsersByIdentifiersQuery(
            ["1234567890"],
            (GibUserDocumentType)99);

        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.DocumentType);
    }
}

public class SearchGibUsersQueryValidatorTests
{
    private readonly SearchGibUsersQueryValidator _validator = new();

    [Fact]
    public void Validate_WithValidSearch_ShouldPass()
    {
        var query = new SearchGibUsersQuery("MERSEL", GibUserDocumentType.EInvoice);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithEmptySearch_ShouldFail()
    {
        var query = new SearchGibUsersQuery("", GibUserDocumentType.EInvoice);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public void Validate_WithTooLongSearch_ShouldFail()
    {
        var longSearch = new string('A', 201);
        var query = new SearchGibUsersQuery(longSearch, GibUserDocumentType.EInvoice);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Search);
    }

    [Fact]
    public void Validate_With200CharSearch_ShouldPass()
    {
        var search = new string('A', 200);
        var query = new SearchGibUsersQuery(search, GibUserDocumentType.EInvoice);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.Search);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidPage_ShouldFail(int page)
    {
        var query = new SearchGibUsersQuery("test", GibUserDocumentType.EInvoice, Page: page);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_WithInvalidPageSize_ShouldFail(int pageSize)
    {
        var query = new SearchGibUsersQuery("test", GibUserDocumentType.EInvoice, PageSize: pageSize);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_WithValidPageSize_ShouldPass(int pageSize)
    {
        var query = new SearchGibUsersQuery("test", GibUserDocumentType.EInvoice, PageSize: pageSize);
        var result = _validator.TestValidate(query);
        result.ShouldNotHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_WithInvalidDocumentType_ShouldFail()
    {
        var query = new SearchGibUsersQuery("test", (GibUserDocumentType)99);
        var result = _validator.TestValidate(query);
        result.ShouldHaveValidationErrorFor(x => x.DocumentType);
    }
}
