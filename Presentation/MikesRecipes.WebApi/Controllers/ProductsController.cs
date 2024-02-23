﻿using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using MikesRecipes.Services;
using MikesRecipes.WebApi.Constants;
using System.ComponentModel.DataAnnotations;

namespace MikesRecipes.WebApi.Controllers;

[ApiVersion(ApiVersions.V1Dot0)]
public class ProductsController(IProductService productService) : BaseController
{
    private readonly IProductService _productService = productService;

    [HttpGet]
    public async Task<IActionResult> GetByTitleSearchTerm([Required][FromQuery] string searchByTitleTerm, CancellationToken cancellationToken)
    {
        var result = await _productService.GetByTitleAsync(searchByTitleTerm, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Errors);
    }
}