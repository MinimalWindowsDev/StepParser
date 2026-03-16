namespace StepParser.Parser;

public sealed record Ap242SemanticSummary(
    bool IsAp242,
    bool HasModelBasedDefinition,
    bool HasPmi,
    bool HasGdt,
    IReadOnlyDictionary<string, int> CategoryCounts,
    IReadOnlyList<PmiDimensionSummary> Dimensions,
    IReadOnlyList<DatumSummary> Datums,
    IReadOnlyList<string> DetectedEntityTypes);

public sealed record PmiDimensionSummary(
    int Id,
    string EntityType,
    string? Name,
    int? TargetAspectId,
    double? NominalValue,
    double? UpperTolerance,
    double? LowerTolerance);

public sealed record DatumSummary(
    int Id,
    string? Label,
    string? Description,
    int? TargetEntityId);

internal static class Ap242SemanticModelBuilder
{
    // -------------------------------------------------------------------------
    // Module 1 — Geometry
    // Curves, surfaces, points, placements and supporting geometry entities
    // from ISO 10303-42 and AP242 integrated resources.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> GeometryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Points
        "CARTESIAN_POINT",
        "POINT_ON_CURVE",
        "POINT_ON_SURFACE",
        "POINT_REPLICA",
        "DEGENERATE_PCURVE",
        "EVALUATED_DEGENERATE_PCURVE",
        // Direction & vector
        "DIRECTION",
        "VECTOR",
        // Placements & transforms
        "AXIS1_PLACEMENT",
        "AXIS2_PLACEMENT_2D",
        "AXIS2_PLACEMENT_3D",
        "CARTESIAN_TRANSFORMATION_OPERATOR",
        "CARTESIAN_TRANSFORMATION_OPERATOR_2D",
        "CARTESIAN_TRANSFORMATION_OPERATOR_3D",
        // Curves — elementary
        "LINE",
        "CIRCLE",
        "ELLIPSE",
        "HYPERBOLA",
        "PARABOLA",
        // Curves — bounded
        "TRIMMED_CURVE",
        "BOUNDED_CURVE",
        // Curves — B-spline family
        "B_SPLINE_CURVE",
        "B_SPLINE_CURVE_WITH_KNOTS",
        "BEZIER_CURVE",
        "RATIONAL_B_SPLINE_CURVE",
        "UNIFORM_CURVE",
        "QUASI_UNIFORM_CURVE",
        // Curves — composite / offset / intersection
        "COMPOSITE_CURVE",
        "COMPOSITE_CURVE_SEGMENT",
        "COMPOSITE_CURVE_ON_SURFACE",
        "REPARAMETRISED_COMPOSITE_CURVE_SEGMENT",
        "OFFSET_CURVE_2D",
        "OFFSET_CURVE_3D",
        "INTERSECTION_CURVE",
        "SEAM_CURVE",
        "SURFACE_CURVE",
        "PCURVE",
        "POLYLINE",
        // Surfaces — elementary
        "PLANE",
        "CYLINDRICAL_SURFACE",
        "CONICAL_SURFACE",
        "SPHERICAL_SURFACE",
        "TOROIDAL_SURFACE",
        "DEGENERATE_TOROIDAL_SURFACE",
        // Surfaces — swept
        "SWEPT_SURFACE",
        "SURFACE_OF_LINEAR_EXTRUSION",
        "SURFACE_OF_REVOLUTION",
        // Surfaces — bounded
        "BOUNDED_SURFACE",
        "RECTANGULAR_TRIMMED_SURFACE",
        "CURVE_BOUNDED_SURFACE",
        "RECTANGULAR_COMPOSITE_SURFACE",
        // Surfaces — B-spline family
        "B_SPLINE_SURFACE",
        "B_SPLINE_SURFACE_WITH_KNOTS",
        "BEZIER_SURFACE",
        "RATIONAL_B_SPLINE_SURFACE",
        "UNIFORM_SURFACE",
        "QUASI_UNIFORM_SURFACE",
        // Surfaces — offset
        "OFFSET_SURFACE",
        // Geometry sets
        "GEOMETRIC_SET",
        "GEOMETRIC_CURVE_SET",
        // Solid geometry primitives (CSG)
        "BLOCK",
        "CYCLIDE_SEGMENT_SOLID",
        "ECCENTRIC_CONE",
        "ELLIPSOID",
        "FACETED_PRIMITIVE",
        "HALF_SPACE_SOLID",
        "ORIENTED_HALF_SPACE",
        "RIGHT_ANGULAR_WEDGE",
        "RIGHT_CIRCULAR_CONE",
        "RIGHT_CIRCULAR_CYLINDER",
        "SPHERE",
        "TORUS",
        "BOX_DOMAIN",
        // Solid operations
        "BOOLEAN_RESULT",
        "CSG_SOLID",
        "SWEPT_AREA_SOLID",
        "EXTRUDED_AREA_SOLID",
        "REVOLVED_AREA_SOLID",
        "SWEPT_FACE_SOLID",
        "EXTRUDED_FACE_SOLID",
        "REVOLVED_FACE_SOLID",
        "SURFACE_OF_LINEAR_EXTRUSION",
        "SOLID_REPLICA",
        // Tessellation
        "COORDINATES_LIST",
        "TRIANGULATED_FACE",
        "COMPLEX_TRIANGULATED_FACE",
        "TRIANGULATED_SURFACE_SET",
        "COMPLEX_TRIANGULATED_SURFACE_SET",
        "TESSELLATED_CURVE_SET",
        "TESSELLATED_GEOMETRIC_SET",
        "TESSELLATED_ITEM",
        "TESSELLATED_SHELL",
        "TESSELLATED_SOLID",
        "TESSELLATED_WIRE",
        "TESSELLATED_FACE",
        "TESSELLATED_CONNECTING_EDGE",
        "TESSELLATED_EDGE",
        "TESSELLATED_POINT_SET",
        "TESSELLATED_VERTEX",
    };

    // -------------------------------------------------------------------------
    // Module 2 — Topology
    // Shells, faces, edges, vertices from ISO 10303-42.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> TopologyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Vertices
        "VERTEX",
        "VERTEX_POINT",
        "SUBVERTEX",
        // Edges
        "EDGE",
        "EDGE_CURVE",
        "ORIENTED_EDGE",
        "SUBEDGE",
        // Loops
        "LOOP",
        "EDGE_LOOP",
        "VERTEX_LOOP",
        "POLY_LOOP",
        "LOOP_AND_POINT",
        // Faces
        "FACE",
        "FACE_SURFACE",
        "FACE_BOUND",
        "FACE_OUTER_BOUND",
        "ADVANCED_FACE",
        "ORIENTED_FACE",
        // Paths & wires
        "PATH",
        "WIRE",
        "OPEN_PATH",
        "CONNECTED_EDGE_SET",
        "CONNECTED_EDGE_SUB_SET",
        // Shells
        "CONNECTED_FACE_SET",
        "OPEN_SHELL",
        "CLOSED_SHELL",
        "ORIENTED_CLOSED_SHELL",
        "ORIENTED_OPEN_SHELL",
        "CONNECTED_FACE_SUB_SET",
        // Solid topology
        "SHELL_BASED_SURFACE_MODEL",
        "SHELL_BASED_WIREFRAME_MODEL",
        "FACETED_BREP",
        "MANIFOLD_SOLID_BREP",
        "BREP_WITH_VOIDS",
        "SOLID_MODEL",
        "EDGE_BASED_WIREFRAME_MODEL",
        "FACE_BASED_SURFACE_MODEL",
        // Generic topology
        "TOPOLOGICAL_REPRESENTATIVE_ITEM",
    };

    // -------------------------------------------------------------------------
    // Module 3 — Representation
    // Shape representations, contexts, maps and styled items.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> RepresentationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Base representation framework
        "REPRESENTATION",
        "REPRESENTATION_CONTEXT",
        "REPRESENTATION_ITEM",
        "REPRESENTATION_MAP",
        "MAPPED_ITEM",
        "REPRESENTATION_RELATIONSHIP",
        "REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION",
        // Contexts
        "GEOMETRIC_REPRESENTATION_CONTEXT",
        "GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT",
        "GLOBAL_UNIT_ASSIGNED_CONTEXT",
        "PARAMETRIC_REPRESENTATION_CONTEXT",
        // Shape representations
        "SHAPE_REPRESENTATION",
        "SHAPE_REPRESENTATION_RELATIONSHIP",
        "ADVANCED_BREP_SHAPE_REPRESENTATION",
        "MANIFOLD_SURFACE_SHAPE_REPRESENTATION",
        "GEOMETRICALLY_BOUNDED_SURFACE_SHAPE_REPRESENTATION",
        "GEOMETRICALLY_BOUNDED_WIREFRAME_SHAPE_REPRESENTATION",
        "FACETED_BREP_SHAPE_REPRESENTATION",
        "EDGE_BASED_WIREFRAME_SHAPE_REPRESENTATION",
        "SHELL_BASED_WIREFRAME_SHAPE_REPRESENTATION",
        "CSG_SHAPE_REPRESENTATION",
        "SOLID_MODEL_SHAPE_REPRESENTATION",
        "TOPOLOGICAL_REPRESENTATION_ITEM",
        // Draughting / MBD representations
        "DRAUGHTING_MODEL",
        "MECHANICAL_DESIGN_GEOMETRIC_PRESENTATION_REPRESENTATION",
        "MECHANICAL_DESIGN_PRESENTATION_REPRESENTATION_WITH_DRAUGHTING",
        // Tessellated shape representations
        "TESSELLATED_SHAPE_REPRESENTATION",
        "TESSELLATED_SHAPE_REPRESENTATION_WITH_ACCURACY_PARAMETERS",
        // Transforms
        "ITEM_DEFINED_TRANSFORMATION",
        "FUNCTIONALLY_DEFINED_TRANSFORMATION",
        // Shape definition
        "SHAPE_DEFINITION_REPRESENTATION",
        // Styling
        "STYLED_ITEM",
        "OVER_RIDING_STYLED_ITEM",
        "PRESENTATION_STYLE_ASSIGNMENT",
        "PRESENTATION_STYLE_BY_CONTEXT",
    };

    // -------------------------------------------------------------------------
    // Module 4 — Product / Application Structure
    // Products, definitions, formations and application contexts.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> ProductStructureTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Application framework
        "APPLICATION_CONTEXT",
        "APPLICATION_PROTOCOL_DEFINITION",
        // Product
        "PRODUCT",
        "PRODUCT_CONTEXT",
        "PRODUCT_CATEGORY",
        "PRODUCT_CATEGORY_RELATIONSHIP",
        "PRODUCT_RELATED_PRODUCT_CATEGORY",
        // Product definition
        "PRODUCT_DEFINITION",
        "PRODUCT_DEFINITION_CONTEXT",
        "PRODUCT_DEFINITION_FORMATION",
        "PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE",
        "PRODUCT_DEFINITION_FORMATION_RELATIONSHIP",
        "PRODUCT_DEFINITION_RELATIONSHIP",
        "PRODUCT_DEFINITION_USAGE",
        "PRODUCT_DEFINITION_WITH_ASSOCIATED_DOCUMENTS",
        "PRODUCT_DEFINITION_OCCURRENCE",
        "PRODUCT_DEFINITION_OCCURRENCE_RELATIONSHIP",
        // Make-from / usage
        "MAKE_FROM_USAGE_OPTION",
        "QUANTIFIED_ASSEMBLY_COMPONENT_USAGE",
        "SPECIFIED_HIGHER_USAGE_OCCURRENCE",
        // Configuration management
        "CONFIGURATION_DESIGN",
        "CONFIGURATION_ITEM",
        "DESIGN_MAKE_FROM_RELATIONSHIP",
        "CONFIGURATION_EFFECTIVITY",
        // Lifecycle
        "LIFE_CYCLE_ENVIRONMENT_CHANGE_REQUEST",
        // Security classification
        "SECURITY_CLASSIFICATION",
        "SECURITY_CLASSIFICATION_ASSIGNMENT",
        "SECURITY_CLASSIFICATION_LEVEL",
        // Date / person / org (common context entities)
        "PERSON",
        "ORGANIZATION",
        "PERSON_AND_ORGANIZATION",
        "PERSON_AND_ORGANIZATION_ROLE",
        "ORGANIZATIONAL_ADDRESS",
        "PERSONAL_ADDRESS",
        "APPROVAL",
        "APPROVAL_DATE_TIME",
        "APPROVAL_PERSON_ORGANIZATION",
        "APPROVAL_ROLE",
        "APPROVAL_STATUS",
        "DATE_AND_TIME",
        "DATE_AND_TIME_ASSIGNMENT",
        "LOCAL_DATE",
        "LOCAL_TIME",
        "COORDINATED_UNIVERSAL_TIME_OFFSET",
        "CALENDAR_DATE",
        // Identifiers / versions
        "IDENTIFIER_ASSIGNMENT",
        "EXTERNAL_SOURCE",
        "EXTERNAL_IDENTIFICATION_ASSIGNMENT",
        "APPLIED_EXTERNAL_IDENTIFICATION_ASSIGNMENT",
    };

    // -------------------------------------------------------------------------
    // Module 5 — Assembly
    // Assembly usage, occurrences, shape and effectivity.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> AssemblyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Assembly usage occurrences
        "ASSEMBLY_COMPONENT_USAGE",
        "NEXT_ASSEMBLY_USAGE_OCCURRENCE",
        "PROMISSORY_USAGE_OCCURRENCE",
        "NEXT_HIGHER_ASSEMBLY_USAGE_OCCURRENCE",
        // Substitutes / alternates
        "ALTERNATE_PRODUCT_RELATIONSHIP",
        "PRODUCT_DEFINITION_SUBSTITUTE",
        "ASSEMBLY_COMPONENT_USAGE_SUBSTITUTE",
        // Shape in context
        "CONTEXT_DEPENDENT_SHAPE_REPRESENTATION",
        "PRODUCT_DEFINITION_SHAPE",
        // Shape aspects
        "SHAPE_ASPECT",
        "SHAPE_ASPECT_RELATIONSHIP",
        "SHAPE_ASPECT_DERIVING_RELATIONSHIP",
        // Effectivity
        "PRODUCT_DEFINITION_EFFECTIVITY",
        "SERIAL_NUMBERED_EFFECTIVITY",
        "DATED_EFFECTIVITY",
        "LOT_EFFECTIVITY",
        // Component ordering
        "COMPONENT_ORDER",
    };

    // -------------------------------------------------------------------------
    // Module 6 — Units & Measurement
    // SI units, conversions, measures, qualifiers.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> UnitsMeasurementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unit definitions
        "NAMED_UNIT",
        "SI_UNIT",
        "CONVERSION_BASED_UNIT",
        "CONTEXT_DEPENDENT_UNIT",
        "DERIVED_UNIT",
        "DERIVED_UNIT_ELEMENT",
        "DIMENSIONAL_EXPONENTS",
        // Measures with units
        "MEASURE_WITH_UNIT",
        "LENGTH_MEASURE_WITH_UNIT",
        "PLANE_ANGLE_MEASURE_WITH_UNIT",
        "SOLID_ANGLE_MEASURE_WITH_UNIT",
        "AREA_MEASURE_WITH_UNIT",
        "VOLUME_MEASURE_WITH_UNIT",
        "MASS_MEASURE_WITH_UNIT",
        "RATIO_MEASURE_WITH_UNIT",
        "TIME_MEASURE_WITH_UNIT",
        "THERMODYNAMIC_TEMPERATURE_MEASURE_WITH_UNIT",
        "ELECTRIC_CURRENT_MEASURE_WITH_UNIT",
        "AMOUNT_OF_SUBSTANCE_MEASURE_WITH_UNIT",
        "LUMINOUS_INTENSITY_MEASURE_WITH_UNIT",
        "ACCELERATION_MEASURE_WITH_UNIT",
        "FORCE_MEASURE_WITH_UNIT",
        "PRESSURE_MEASURE_WITH_UNIT",
        "FREQUENCY_MEASURE_WITH_UNIT",
        "KINEMATIC_VISCOSITY_MEASURE_WITH_UNIT",
        "DYNAMIC_VISCOSITY_MEASURE_WITH_UNIT",
        "POWER_MEASURE_WITH_UNIT",
        "ELECTRIC_CHARGE_MEASURE_WITH_UNIT",
        "ELECTRIC_POTENTIAL_MEASURE_WITH_UNIT",
        "ELECTRIC_RESISTANCE_MEASURE_WITH_UNIT",
        "ENERGY_MEASURE_WITH_UNIT",
        "ILLUMINANCE_MEASURE_WITH_UNIT",
        "INDUCTANCE_MEASURE_WITH_UNIT",
        "LUMINOUS_FLUX_MEASURE_WITH_UNIT",
        "MAGNETIC_FLUX_DENSITY_MEASURE_WITH_UNIT",
        "MAGNETIC_FLUX_MEASURE_WITH_UNIT",
        "RADIOACTIVITY_MEASURE_WITH_UNIT",
        // Measure scalars (without units — used as representation items)
        "MEASURE_REPRESENTATION_ITEM",
        "VALUE_REPRESENTATION_ITEM",
        "DESCRIPTIVE_REPRESENTATION_ITEM",
        "COUNT_MEASURE",
        "PARAMETER_VALUE",
        "LENGTH_MEASURE",
        "PLANE_ANGLE_MEASURE",
        "SOLID_ANGLE_MEASURE",
        "AREA_MEASURE",
        "VOLUME_MEASURE",
        "MASS_MEASURE",
        "RATIO_MEASURE",
        "TIME_MEASURE",
        "THERMODYNAMIC_TEMPERATURE_MEASURE",
        "ELECTRIC_CURRENT_MEASURE",
        "AMOUNT_OF_SUBSTANCE_MEASURE",
        "LUMINOUS_INTENSITY_MEASURE",
        // Uncertainty
        "UNCERTAINTY_MEASURE_WITH_UNIT",
        // Qualifiers
        "QUALIFIED_REPRESENTATION_ITEM",
        "PRECISION_QUALIFIER",
        "TYPE_QUALIFIER",
        "UPPER_BOUND_QUALIFIER",
        "LOWER_BOUND_QUALIFIER",
        "NOT_YET_REFERENCED_ROLE",
    };

    // -------------------------------------------------------------------------
    // Module 7 — PMI Semantic: Dimensions
    // Dimensional characteristics per ISO 10303-47 / AP242 MBD.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> PmiDimensionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Dimensional characteristic representations
        "DIMENSIONAL_CHARACTERISTIC_REPRESENTATION",
        "SHAPE_DIMENSION_REPRESENTATION",
        "SHAPE_DIMENSION_REPRESENTATION_WITH_PARAMETERS",
        // Dimensional locations
        "DIMENSIONAL_LOCATION",
        "DIMENSIONAL_LOCATION_WITH_PATH",
        "ANGULAR_LOCATION",
        "DIRECTED_DIMENSIONAL_LOCATION",
        "DIMENSIONAL_LOCATION_RADIUS",
        // Dimensional sizes
        "DIMENSIONAL_SIZE",
        "DIMENSIONAL_SIZE_WITH_PATH",
        "ANGULAR_SIZE",
        "LINEAR_DIMENSION",
        "RADIUS_DIMENSION",
        "DIAMETER_DIMENSION",
        "CHAMFER_DIMENSION",
        "CURVE_DIMENSION",
        "ANGULAR_DIMENSION",
        // Dimension curves & terminators
        "DIMENSION_CURVE",
        "DIMENSION_CURVE_TERMINATOR",
        "LEADER_CURVE",
        "PROJECTION_CURVE",
        "TERMINATOR_SYMBOL",
        // Tolerance bounds
        "PLUS_MINUS_BOUNDS",
        "LIMITS_AND_FITS",
        "TOLERANCE_VALUE",
        // Pairs and relations
        "DIMENSION_PAIR",
        "GENERAL_DATUM_REFERENCE",
        "DIMENSION_RELATED_TOLERANCE_ZONE_ELEMENT",
    };

    // -------------------------------------------------------------------------
    // Module 8 — PMI Semantic: Geometric Tolerances
    // GD&T tolerances per ISO 10303-47 / AP242 MBD.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> PmiGeometricToleranceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Base geometric tolerance
        "GEOMETRIC_TOLERANCE",
        "GEOMETRIC_TOLERANCE_WITH_DATUM_REFERENCE",
        "GEOMETRIC_TOLERANCE_WITH_DEFINED_AREA_UNIT",
        "GEOMETRIC_TOLERANCE_WITH_DEFINED_UNIT",
        "GEOMETRIC_TOLERANCE_WITH_MAXIMUM_TOLERANCE",
        "GEOMETRIC_TOLERANCE_WITH_MODIFIERS",
        "MODIFIED_GEOMETRIC_TOLERANCE",
        "UNEQUALLY_DISPOSED_GEOMETRIC_TOLERANCE",
        "GEOMETRIC_TOLERANCE_RELATIONSHIP",
        // Form tolerances
        "STRAIGHTNESS_TOLERANCE",
        "FLATNESS_TOLERANCE",
        "CIRCULARITY_TOLERANCE",
        "CYLINDRICITY_TOLERANCE",
        // Profile tolerances
        "LINE_PROFILE_TOLERANCE",
        "SURFACE_PROFILE_TOLERANCE",
        // Orientation tolerances
        "ANGULARITY_TOLERANCE",
        "PERPENDICULARITY_TOLERANCE",
        "PARALLELISM_TOLERANCE",
        // Location tolerances
        "POSITION_TOLERANCE",
        "CONCENTRICITY_TOLERANCE",
        "COAXIALITY_TOLERANCE",
        "SYMMETRY_TOLERANCE",
        // Runout tolerances
        "CIRCULAR_RUNOUT_TOLERANCE",
        "TOTAL_RUNOUT_TOLERANCE",
        // Tolerance zones
        "TOLERANCE_ZONE",
        "TOLERANCE_ZONE_DEFINITION",
        "TOLERANCE_ZONE_FORM",
        "NON_UNIFORM_ZONE_DEFINITION",
        "RUNOUT_ZONE_DEFINITION",
        "RUNOUT_ZONE_ORIENTATION",
        "RUNOUT_ZONE_ORIENTATION_REFERENCE_DIRECTION",
        "PROJECTED_ZONE_DEFINITION",
        // Composite shape aspects used in GD&T
        "COMPOSITE_SHAPE_ASPECT",
        "COMPOSITE_GROUP_SHAPE_ASPECT",
        "CONTINUOUS_SHAPE_ASPECT",
    };

    // -------------------------------------------------------------------------
    // Module 9 — PMI Semantic: Datums
    // Datum features, references, systems and targets per AP242 MBD.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> PmiDatumTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Datum entities
        "DATUM",
        "DATUM_FEATURE",
        "COMMON_DATUM",
        "DATUM_SYSTEM",
        // Datum references
        "DATUM_REFERENCE",
        "DATUM_REFERENCE_COMPARTMENT",
        "DATUM_REFERENCE_ELEMENT",
        "DATUM_REFERENCE_MODIFIER_WITH_VALUE",
        "DATUM_REFERENCE_MODIFIER",
        "REFERENCED_MODIFIED_DATUM",
        // Datum targets
        "DATUM_TARGET",
        "PLACED_DATUM_TARGET_FEATURE",
        "CIRCULAR_DATUM_TARGET",
        "RECTANGULAR_DATUM_TARGET",
        "LINE_DATUM_TARGET",
        "POINT_DATUM_TARGET",
        "AREA_DATUM_TARGET",
        // Geometric relations used with datums
        "GEOMETRIC_ALIGNMENT",
        "GEOMETRIC_INTERSECTION",
        "PARALLEL_OFFSET",
        "PERPENDICULAR_TO",
        "TANGENT",
    };

    // -------------------------------------------------------------------------
    // Module 10 — PMI Semantic: Shape Aspects & Tolerance Zones
    // Shape aspect subtypes used in AP242 MBD feature and tolerance modelling.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> PmiShapeAspectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core shape aspect
        "SHAPE_ASPECT",
        "SHAPE_ASPECT_RELATIONSHIP",
        "SHAPE_ASPECT_DERIVING_RELATIONSHIP",
        "DERIVED_SHAPE_ASPECT",
        // Symmetry / pattern
        "CENTRE_OF_SYMMETRY",
        "SYMMETRIC_SHAPE_ASPECT",
        "CYCLIC_SYMMETRY",
        "ROTATIONAL_SYMMETRY",
        "PATTERN_OFFSET_SHAPE_ASPECT",
        "REPLICATION_FEATURE",
        // Connectivity and coincidence
        "COINCIDENT_SHAPE_ASPECT",
        "INCIDENCE_SHAPE_ASPECT",
        "CONTACT_FEATURE",
        "INTERFACE_COMPONENT",
        // Geometry-typed shape aspects
        "EDGE_SHAPE_ASPECT",
        "FACE_SHAPE_ASPECT",
        "LINE_SHAPE_ASPECT",
        "SURFACE_SHAPE_ASPECT",
        "VOLUME_SHAPE_ASPECT",
        "APEX",
        "EXTENSION",
        // Grouping
        "GROUP_SHAPE_ASPECT",
        "COMPOSITE_SHAPE_ASPECT",
        "COMPOSITE_GROUP_SHAPE_ASPECT",
        "CONTINUOUS_SHAPE_ASPECT",
        "ALL_AROUND_SHAPE_ASPECT",
        "ALL_OVER_SHAPE_ASPECT",
        "BETWEEN_SHAPE_ASPECT",
        // Feature types
        "THREAD",
        "NON_FEATURE_DEFINITION",
        "GENERAL_SHAPE_ASPECT",
        "GENERIC_SHAPE_ASPECT",
        // Tolerance zones (referenced in this context)
        "TOLERANCE_ZONE",
        "TOLERANCE_ZONE_DEFINITION",
        "TOLERANCE_ZONE_FORM",
    };

    // -------------------------------------------------------------------------
    // Module 11 — PMI Presentation / Annotations (Draughting)
    // Annotation occurrences, draughting model, presentation styles, camera,
    // symbols and text from ISO 10303-46, -101 and AP242 MBD.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> PmiPresentationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Annotation occurrences
        "ANNOTATION_OCCURRENCE",
        "ANNOTATION_CURVE_OCCURRENCE",
        "ANNOTATION_FILL_AREA_OCCURRENCE",
        "ANNOTATION_PLACEHOLDER_OCCURRENCE",
        "ANNOTATION_SUBFIGURE_OCCURRENCE",
        "ANNOTATION_TEXT",
        "ANNOTATION_TEXT_OCCURRENCE",
        "ANNOTATION_PLANE",
        "TESSELLATED_ANNOTATION_OCCURRENCE",
        "DRAUGHTING_ANNOTATION_OCCURRENCE",
        // Draughting callout / model
        "DRAUGHTING_CALLOUT",
        "DRAUGHTING_ELEMENTS",
        "DRAUGHTING_MODEL",
        "DRAUGHTING_SPECIFICATION_REFERENCE",
        "DRAUGHTING_SUBTITLE",
        "DRAUGHTING_TITLE",
        "DRAUGHTING_PRE_DEFINED_COLOUR",
        "DRAUGHTING_PRE_DEFINED_CURVE_FONT",
        "DRAUGHTING_PRE_DEFINED_TEXT_FONT",
        // Presentation framework
        "PRESENTATION_AREA",
        "PRESENTATION_LAYER_ASSIGNMENT",
        "PRESENTATION_LAYER_USAGE",
        "PRESENTATION_REPRESENTATION",
        "PRESENTATION_SET",
        "PRESENTATION_SIZE",
        "PRESENTATION_STYLE_ASSIGNMENT",
        "PRESENTATION_STYLE_BY_CONTEXT",
        "PRESENTATION_VIEW",
        // Camera & view volume
        "CAMERA_IMAGE",
        "CAMERA_IMAGE_2D_WITH_SCALE",
        "CAMERA_IMAGE_3D_WITH_SCALE",
        "CAMERA_MODEL",
        "CAMERA_MODEL_D2",
        "CAMERA_MODEL_D3",
        "CAMERA_MODEL_D3_WITH_HLHSR",
        "CAMERA_USAGE",
        "VIEW_VOLUME",
        "PLANAR_BOX",
        "PLANAR_EXTENT",
        // Curve styles
        "CURVE_STYLE",
        "CURVE_STYLE_FONT",
        "CURVE_STYLE_FONT_AND_SCALING",
        "CURVE_STYLE_FONT_PATTERN",
        "CURVE_STYLE_WITH_ENDS",
        "PRE_DEFINED_CURVE_FONT",
        // Fill area styles
        "FILL_AREA_STYLE",
        "FILL_AREA_STYLE_COLOUR",
        "FILL_AREA_STYLE_HATCHING",
        "FILL_AREA_STYLE_TILES",
        // Point styles
        "POINT_STYLE",
        // Surface styles
        "SURFACE_SIDE_STYLE",
        "SURFACE_STYLE_BOUNDARY",
        "SURFACE_STYLE_CONTROL_GRID",
        "SURFACE_STYLE_FILL_AREA",
        "SURFACE_STYLE_PARAMETER_LINE",
        "SURFACE_STYLE_REFLECTANCE_AMBIENT",
        "SURFACE_STYLE_RENDERING",
        "SURFACE_STYLE_SEGMENTATION_CURVE",
        "SURFACE_STYLE_SILHOUETTE",
        "SURFACE_STYLE_USAGE",
        "SURFACE_STYLE_TRANSPARENT",
        // Text styles
        "TEXT_STYLE",
        "TEXT_STYLE_FOR_DEFINED_FONT",
        "TEXT_STYLE_WITH_BOX_CHARACTERISTICS",
        "TEXT_STYLE_WITH_MIRROR",
        "TEXT_STYLE_WITH_SPACING",
        "PRE_DEFINED_TEXT_FONT",
        // Colours
        "COLOUR_RGB",
        "COLOUR_SPECIFICATION",
        "PRE_DEFINED_COLOUR",
        "DRAUGHTING_PRE_DEFINED_COLOUR",
        // Symbols
        "SYMBOL_COLOUR",
        "SYMBOL_REPRESENTATION",
        "SYMBOL_REPRESENTATION_MAP",
        "SYMBOL_STYLE",
        "SYMBOL_TARGET",
        "PRE_DEFINED_SYMBOL",
        "TERMINATOR_SYMBOL",
        // Dimension curves & leaders (presentation side)
        "DIMENSION_CURVE",
        "DIMENSION_CURVE_TERMINATOR",
        "LEADER_CURVE",
        "PROJECTION_CURVE",
    };

    // -------------------------------------------------------------------------
    // Module 12 — Materials / Properties
    // Material designation, property definitions, documents and mass properties.
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> MaterialsPropertiesTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Material designation
        "MATERIAL_DESIGNATION",
        "MATERIAL_DESIGNATION_CHARACTERIZATION",
        // Properties
        "PROPERTY_DEFINITION",
        "PROPERTY_DEFINITION_REPRESENTATION",
        "PROPERTY_DEFINITION_RELATIONSHIP",
        "CHARACTERIZED_OBJECT",
        "CHARACTERIZED_PRODUCT_DEFINITION",
        // Material specific
        "MATERIAL_PROPERTY",
        "MATERIAL_PROPERTY_REPRESENTATION",
        "PRODUCT_MATERIAL_COMPOSITION_RELATIONSHIP",
        // Mass / inertia
        "MASS_PROPERTY",
        "CENTRE_OF_MASS",
        "INERTIA_TENSOR",
        // Surface properties
        "SURFACE_CONDITION",
        "SURFACE_TEXTURE_REPRESENTATION",
        "FINISH",
        "HARDNESS",
        "BRINELL_HARDNESS",
        "VICKERS_HARDNESS",
        "KNOOP_HARDNESS",
        "ROCKWELL_HARDNESS",
        // Documents
        "DOCUMENT",
        "DOCUMENT_FILE",
        "DOCUMENT_REFERENCE",
        "DOCUMENT_RELATIONSHIP",
        "DOCUMENT_USAGE_CONSTRAINT",
        "APPLIED_DOCUMENT_REFERENCE",
        "APPLIED_DOCUMENT_USAGE_CONSTRAINT_ASSIGNMENT",
        "DOCUMENT_WITH_CLASS",
        "DOCUMENT_TYPE",
        // External identifiers
        "APPLIED_EXTERNAL_IDENTIFICATION_ASSIGNMENT",
        "EXTERNAL_IDENTIFICATION_ASSIGNMENT",
        "FILE",
        // Classification
        "APPLIED_CLASSIFICATION_ASSIGNMENT",
        "CLASSIFICATION_ASSIGNMENT",
        "CLASSIFICATION_ROLE",
    };

    // -------------------------------------------------------------------------
    // Module 13 — Kinematics
    // Kinematic structures, joints, pairs, paths and motion from AP242 ed2+
    // (ISO 10303-105 / Part 514 resource).
    // -------------------------------------------------------------------------
    internal static readonly HashSet<string> KinematicsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Top-level kinematic structure
        "KINEMATIC_STRUCTURE",
        "MECHANISM",
        "MECHANISM_REPRESENTATION",
        "MECHANISM_STATE_REPRESENTATION",
        "KINEMATIC_TOPOLOGY_STRUCTURE",
        "KINEMATIC_TOPOLOGY_SUBSTRUCTURE",
        "KINEMATIC_TOPOLOGY_NETWORK_STRUCTURE",
        "KINEMATIC_LOOP",
        // Links
        "KINEMATIC_LINK",
        "KINEMATIC_LINK_REPRESENTATION",
        "RIGID_LINK_REPRESENTATION",
        "FLEXIBLE_LINK_REPRESENTATION",
        // Joints (abstract)
        "KINEMATIC_JOINT",
        // Lower-pair joints
        "LOW_ORDER_KINEMATIC_PAIR",
        "LOW_ORDER_KINEMATIC_PAIR_WITH_MOTION_COUPLING",
        "LOW_ORDER_KINEMATIC_PAIR_WITH_RANGE",
        "REVOLUTE_PAIR",
        "REVOLUTE_PAIR_WITH_RANGE",
        "PRISMATIC_PAIR",
        "PRISMATIC_PAIR_WITH_RANGE",
        "SCREW_PAIR",
        "SCREW_PAIR_WITH_RANGE",
        "CYLINDRICAL_PAIR",
        "CYLINDRICAL_PAIR_WITH_RANGE",
        "UNIVERSAL_JOINT",
        "SPHERICAL_PAIR",
        "SPHERICAL_PAIR_WITH_PIN",
        "SPHERICAL_PAIR_WITH_RANGE",
        "PLANAR_PAIR",
        "PLANAR_PAIR_WITH_RANGE",
        "FULLY_CONSTRAINED_PAIR",
        // Higher-pair joints
        "HIGH_ORDER_KINEMATIC_PAIR",
        "POINT_ON_SURFACE_PAIR",
        "POINT_ON_SURFACE_PAIR_WITH_RANGE",
        "POINT_ON_PLANAR_CURVE_PAIR",
        "POINT_ON_PLANAR_CURVE_PAIR_WITH_RANGE",
        "SLIDING_CURVE_PAIR",
        "SLIDING_CURVE_PAIR_WITH_RANGE",
        "ROLLING_CURVE_PAIR",
        "ROLLING_CURVE_PAIR_WITH_RANGE",
        "SLIDING_SURFACE_PAIR",
        "SLIDING_SURFACE_PAIR_WITH_RANGE",
        "ROLLING_SURFACE_PAIR",
        "ROLLING_SURFACE_PAIR_WITH_RANGE",
        "GEAR_PAIR",
        "GEAR_PAIR_WITH_RANGE",
        "RACK_AND_PINION_PAIR",
        "RACK_AND_PINION_PAIR_WITH_RANGE",
        // Pair representations
        "PAIR_REPRESENTATION_RELATIONSHIP",
        "REVOLUTE_JOINT",
        "PRISMATIC_JOINT",
        "SCREW_JOINT",
        "CYLINDRICAL_JOINT",
        // Flexibility
        "LINEAR_FLEXIBILITY_RELATION",
        "ROTATIONAL_FLEXIBILITY_RELATION",
        // Motion / path
        "KINEMATIC_PATH",
        "KINEMATIC_PATH_SEGMENT",
        "CONSTRAINED_KINEMATIC_MOTION_REPRESENTATION",
        "UNCONSTRAINED_MOTION",
        "ROTATION_ABOUT_DIRECTION",
        "SIC_CURVE",
        // Interpolation / configuration
        "INTERPOLATED_CONFIGURATION_SEQUENCE",
        "CONFIGURATION_INTERPOLATION",
        // Product binding
        "PRODUCT_DEFINITION_KINEMATICS",
        "PRODUCT_DEFINITION_RELATIONSHIP_KINEMATICS",
        "KINEMATIC_PROPERTY_MECHANISM_REPRESENTATION",
    };

    // -------------------------------------------------------------------------
    // Derived convenience sets — union of all PMI types for fast lookup.
    // -------------------------------------------------------------------------
    private static readonly HashSet<string> PmiEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // PMI Presentation
        "ANNOTATION_OCCURRENCE",
        "ANNOTATION_PLANE",
        "ANNOTATION_TEXT",
        "TESSELLATED_ANNOTATION_OCCURRENCE",
        "DRAUGHTING_CALLOUT",
        "DRAUGHTING_MODEL",
        // PMI Dimensions
        "DIMENSIONAL_SIZE",
        "DIMENSIONAL_LOCATION",
        "ANGULAR_LOCATION",
        "ANGULAR_SIZE",
        "DIMENSIONAL_SIZE_WITH_PATH",
        "DIMENSIONAL_LOCATION_WITH_PATH",
        "RADIUS_DIMENSION",
        "DIAMETER_DIMENSION",
        "CHAMFER_DIMENSION",
        "LINEAR_DIMENSION",
        "ANGULAR_DIMENSION",
        "CURVE_DIMENSION",
        // PMI Geometric Tolerances
        "GEOMETRIC_TOLERANCE",
        "GEOMETRIC_TOLERANCE_WITH_DATUM_REFERENCE",
        "GEOMETRIC_TOLERANCE_WITH_DEFINED_AREA_UNIT",
        "GEOMETRIC_TOLERANCE_WITH_DEFINED_UNIT",
        "GEOMETRIC_TOLERANCE_WITH_MAXIMUM_TOLERANCE",
        "GEOMETRIC_TOLERANCE_WITH_MODIFIERS",
        "MODIFIED_GEOMETRIC_TOLERANCE",
        "UNEQUALLY_DISPOSED_GEOMETRIC_TOLERANCE",
        "POSITION_TOLERANCE",
        "STRAIGHTNESS_TOLERANCE",
        "FLATNESS_TOLERANCE",
        "CIRCULARITY_TOLERANCE",
        "CYLINDRICITY_TOLERANCE",
        "LINE_PROFILE_TOLERANCE",
        "SURFACE_PROFILE_TOLERANCE",
        "ANGULARITY_TOLERANCE",
        "PERPENDICULARITY_TOLERANCE",
        "PARALLELISM_TOLERANCE",
        "CONCENTRICITY_TOLERANCE",
        "COAXIALITY_TOLERANCE",
        "SYMMETRY_TOLERANCE",
        "CIRCULAR_RUNOUT_TOLERANCE",
        "TOTAL_RUNOUT_TOLERANCE",
        // PMI Datums
        "DATUM",
        "DATUM_FEATURE",
        "DATUM_REFERENCE",
        "DATUM_SYSTEM",
        "DATUM_TARGET",
        "PLACED_DATUM_TARGET_FEATURE",
        // Shared
        "SHAPE_ASPECT",
        "MEASURE_REPRESENTATION_ITEM",
        "TOLERANCE_VALUE",
    };

    private static readonly HashSet<string> GdtEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DIMENSIONAL_SIZE",
        "DIMENSIONAL_LOCATION",
        "ANGULAR_LOCATION",
        "ANGULAR_SIZE",
        "DIMENSIONAL_SIZE_WITH_PATH",
        "DIMENSIONAL_LOCATION_WITH_PATH",
        "RADIUS_DIMENSION",
        "DIAMETER_DIMENSION",
        "LINEAR_DIMENSION",
        "GEOMETRIC_TOLERANCE",
        "GEOMETRIC_TOLERANCE_WITH_DATUM_REFERENCE",
        "MODIFIED_GEOMETRIC_TOLERANCE",
        "UNEQUALLY_DISPOSED_GEOMETRIC_TOLERANCE",
        "POSITION_TOLERANCE",
        "STRAIGHTNESS_TOLERANCE",
        "FLATNESS_TOLERANCE",
        "CIRCULARITY_TOLERANCE",
        "CYLINDRICITY_TOLERANCE",
        "LINE_PROFILE_TOLERANCE",
        "SURFACE_PROFILE_TOLERANCE",
        "ANGULARITY_TOLERANCE",
        "PERPENDICULARITY_TOLERANCE",
        "PARALLELISM_TOLERANCE",
        "CONCENTRICITY_TOLERANCE",
        "COAXIALITY_TOLERANCE",
        "SYMMETRY_TOLERANCE",
        "CIRCULAR_RUNOUT_TOLERANCE",
        "TOTAL_RUNOUT_TOLERANCE",
        "DATUM",
        "DATUM_REFERENCE",
        "DATUM_SYSTEM",
        "TOLERANCE_VALUE",
    };

    public static Ap242SemanticSummary Build(StepFile stepFile)
    {
        Dictionary<string, int> categoryCounts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["representation"] = 0,
            ["geometry"] = 0,
            ["pmi"] = 0,
            ["gdt"] = 0
        };

        HashSet<string> detectedTypes = new(StringComparer.OrdinalIgnoreCase);
        List<PmiDimensionSummary> dimensions = new();
        List<DatumSummary> datums = new();
        string schema = stepFile.FileSchema ?? string.Empty;
        bool isAp242 = schema.Contains("AP242", StringComparison.OrdinalIgnoreCase);

        foreach (EntityInstance entity in stepFile.Data.Values)
        {
            foreach (string entityType in EnumerateEntityTypes(entity))
            {
                detectedTypes.Add(entityType);

                if (entityType.Contains("REPRESENTATION", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("CONTEXT", StringComparison.OrdinalIgnoreCase))
                {
                    categoryCounts["representation"]++;
                }

                if (entityType.Contains("FACE", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("BREP", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("SHELL", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("CURVE", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("SURFACE", StringComparison.OrdinalIgnoreCase))
                {
                    categoryCounts["geometry"]++;
                }

                if (PmiEntityTypes.Contains(entityType))
                {
                    categoryCounts["pmi"]++;
                }

                if (GdtEntityTypes.Contains(entityType))
                {
                    categoryCounts["gdt"]++;
                }
            }

            if (string.Equals(entity.Name, "DIMENSIONAL_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                dimensions.Add(BuildDimension(stepFile.Data, entity));
            }

            if (string.Equals(entity.Name, "DATUM", StringComparison.OrdinalIgnoreCase))
            {
                datums.Add(BuildDatum(entity));
            }
        }

        bool hasPmi = categoryCounts["pmi"] > 0;
        bool hasGdt = categoryCounts["gdt"] > 0;
        bool hasMbd = isAp242 && (hasPmi || hasGdt);

        return new Ap242SemanticSummary(
            isAp242,
            hasMbd,
            hasPmi,
            hasGdt,
            categoryCounts,
            dimensions,
            datums,
            detectedTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IEnumerable<string> EnumerateEntityTypes(EntityInstance entity)
    {
        if (entity.IsComplex)
        {
            return entity.Components!.Select(component => component.Name);
        }

        return entity.Name is null ? [] : [entity.Name];
    }

    private static PmiDimensionSummary BuildDimension(
        IReadOnlyDictionary<int, EntityInstance> entities,
        EntityInstance entity)
    {
        int? targetAspectId = TryGetEntityRef(entity.Parameters, 0);
        string? name = TryGetString(entity.Parameters, 1);
        double? nominalValue = null;
        double? upperTolerance = null;
        double? lowerTolerance = null;

        foreach (EntityInstance candidate in entities.Values.OrderBy(e => e.Id))
        {
            if (!string.Equals(candidate.Name, "MEASURE_WITH_UNIT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double? value = TryGetNestedMeasureValue(candidate.Parameters.FirstOrDefault());
            if (value is null)
            {
                continue;
            }

            if (nominalValue is null)
            {
                nominalValue = value;
                continue;
            }

            if (value >= 0 && upperTolerance is null)
            {
                upperTolerance = value;
                continue;
            }

            if (value < 0 && lowerTolerance is null)
            {
                lowerTolerance = value;
            }
        }

        return new PmiDimensionSummary(
            entity.Id,
            entity.Name ?? "DIMENSIONAL_SIZE",
            name,
            targetAspectId,
            nominalValue,
            upperTolerance,
            lowerTolerance);
    }

    private static DatumSummary BuildDatum(EntityInstance entity)
    {
        return new DatumSummary(
            entity.Id,
            TryGetString(entity.Parameters, 0),
            TryGetString(entity.Parameters, 1),
            TryGetEntityRef(entity.Parameters, 2));
    }

    private static int? TryGetEntityRef(IReadOnlyList<Parameter> parameters, int index)
    {
        if (index >= parameters.Count)
        {
            return null;
        }

        return parameters[index] is Parameter.EntityReference entityReference ? entityReference.Id : null;
    }

    private static string? TryGetString(IReadOnlyList<Parameter> parameters, int index)
    {
        if (index >= parameters.Count)
        {
            return null;
        }

        return parameters[index] is Parameter.StringValue stringValue ? stringValue.Value : null;
    }

    private static double? TryGetNestedMeasureValue(Parameter? parameter)
    {
        return parameter switch
        {
            Parameter.TypedValue { Inner: Parameter.RealValue realValue } => realValue.Value,
            Parameter.TypedValue { Inner: Parameter.IntegerValue integerValue } => integerValue.Value,
            Parameter.RealValue realValue => realValue.Value,
            Parameter.IntegerValue integerValue => integerValue.Value,
            _ => null
        };
    }
}
