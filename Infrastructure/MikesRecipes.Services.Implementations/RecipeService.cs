﻿using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MikesRecipes.DAL;
using MikesRecipes.Domain.Models;
using MikesRecipes.Domain.Shared;
using MikesRecipes.Services.Contracts;
using MikesRecipes.Services.Contracts.Common;
using MikesRecipes.Services.Implementations.Extensions;
using System.Data;

namespace MikesRecipes.Services.Implementations;

public class RecipeService : BaseService, IRecipeService
{
    public RecipeService(
        IClock clock, 
        ILogger<BaseService> logger, 
        MikesRecipesDbContext dbContext, 
        IServiceScopeFactory serviceScopeFactory) : base(clock, logger, dbContext, serviceScopeFactory)
    {
    }

    public async Task<Response<RecipesPage>> GetAsync(PagingOptions pagingOptions, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var validationResult = Validate(pagingOptions);
        if (validationResult.IsFailure)
        {
            return Response.Failure<RecipesPage>(validationResult.Error);
        }

        int totalItemsCount = _dbContext.Recipes.Count();
        var recipesDtos = await _dbContext
            .Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .ThenInclude(i => i.Product)
            .OrderBy(e => e.Title)
            .ApplyPaging(pagingOptions)
            .Select(e => new RecipeDTO
                (
                    e.Id,
                    e.Title,
                    e.Url,
                    e.Ingredients.Select(ing => new ProductDTO(ing.ProductId, ing.Product.Title)).ToList()
                ))
            .ToListAsync(cancellationToken);

        return Response.Success(new RecipesPage(recipesDtos, totalItemsCount, pagingOptions));
    }

    public async Task<Response<RecipesPage>> GetByIncludedProductsAsync(
        ByIncludedProductsFilter filter,
        PagingOptions pagingOptions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pagingOptionsValidationResult = Validate(pagingOptions);
        if (pagingOptionsValidationResult.IsFailure)
        {
            return Response.Failure<RecipesPage>(pagingOptionsValidationResult.Error);
        }

        var filterValidationResult = Validate(filter);
        if (pagingOptionsValidationResult.IsFailure)
        {
            return Response.Failure<RecipesPage>(filterValidationResult.Error);
        }

        int includedProductsCount = filter.ProductIds.Count();
        bool isAllProductsExists = _dbContext
            .Products
            .Where(e => filter.ProductIds.Contains(e.Id))
            .Count() == includedProductsCount;
        if (!isAllProductsExists)
        {
            return Response.Failure<RecipesPage>(new Error("Invalid products ids passed."));
        }

        string productsIdsRaw = string.Join(",", filter.ProductIds.Select(e => $"'{e.Value}'"));
        string sql = $@"
             SELECT [r].[{nameof(Recipe.Id)}], [r].[{nameof(Recipe.Title)}], [r].[{nameof(Recipe.Url)}], [r].[{nameof(Recipe.IngredientsCount)}]
             FROM [{nameof(_dbContext.Products)}] AS [p]
             INNER JOIN [{nameof(_dbContext.Ingredients)}] AS [i] ON [p].[{nameof(Product.Id)}] = [i].[{nameof(Ingredient.ProductId)}]
             INNER JOIN [{nameof(_dbContext.Recipes)}] AS [r] ON [i].[{nameof(Ingredient.RecipeId)}] = [r].[{nameof(Recipe.Id)}]
             WHERE [p].[{nameof(Product.Id)}] IN ({productsIdsRaw})
             GROUP BY [r].[{nameof(Recipe.Id)}], [r].[{nameof(Recipe.Title)}], [r].[{nameof(Recipe.Url)}], [r].[{nameof(Recipe.IngredientsCount)}]
             HAVING COUNT(DISTINCT ([i].[{nameof(Ingredient.ProductId)}])) = {includedProductsCount} 
                           AND [r].[{nameof(Recipe.IngredientsCount)}] <= {filter.OtherProductsCount + includedProductsCount}";

        int totalRecipesCount = _dbContext.Recipes.FromSqlRaw(sql).Count();
        var result = await _dbContext
            .Recipes
            .FromSqlRaw(sql)
            .AsNoTracking()
            .OrderBy(e => e.Title)
            .ApplyPaging(pagingOptions)
            .Select(e => new RecipeDTO
                (
                    e.Id,
                    e.Title,
                    e.Url,
                    e.Ingredients.Select(pr => new ProductDTO(pr.ProductId, pr.Product.Title)).ToList()
                ))
            .ToListAsync(cancellationToken);

        return Response.Success(new RecipesPage(result, totalRecipesCount, pagingOptions));
    }
}
