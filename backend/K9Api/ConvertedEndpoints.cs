public static class ConvertedEndpoints
{
    public static void MapConvertedEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Json(new
        {
            message = "MES API is running",
            endpoints = new[]
            {
                "/api/users",
                "/api/sn-types",
                "/api/items",
                "/api/item-revisions",
                "/api/routing",
                "/api/workflow",
                "/api/stations",
                "/api/work-orders",
                "/api/sites",
                "/api/station/check-sn",
                "/api/traceability",
                "/api/labels",
                "/api/sgd-pos"
            }
        }));

        SitesEndpoints.MapSites(app);
        AuthEndpoints.MapAuth(app);
        StationsEndpoints.MapStations(app);
        StationValidationEndpoints.MapStationValidation(app);
        ItemsEndpoints.MapItems(app);
        ItemRevisionsEndpoints.MapItemRevisions(app);
        EpvTypesEndpoints.MapEpvTypes(app);
        SnTypesEndpoints.MapSnTypes(app);
        SgdPosEndpoints.MapSgdPos(app);
        BomEndpoints.MapBom(app);
        RoutingEndpoints.MapRouting(app);
        WorkflowEndpoints.MapWorkflow(app);
        WorkOrdersEndpoints.MapWorkOrders(app);
        GenerateSnEndpoints.MapGenerateSn(app);
        TraceabilityEndpoints.MapTraceability(app);
        PackingEndpoints.MapPacking(app);
        AssemblyEndpoints.MapAssembly(app);
        OperationsRouteBackEndpoints.MapOperationsRouteBack(app);
        LabelsEndpoints.MapLabels(app);
        ReportsEndpoints.MapReports(app);
    }
}
