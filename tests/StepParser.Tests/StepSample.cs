namespace StepParser.Tests;

/// <summary>
/// Inline Part-21 fixtures. Each one is deliberately minimal and named for the single construct it
/// exercises, so a failure names the feature rather than "the big sample broke".
/// </summary>
internal static class StepSample
{
    /// <summary>Well-formed AP214 file, 11 simple entities. The baseline happy path.</summary>
    public const string Minimal = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('A minimal AP214 example'),'2;1');
        FILE_NAME('demo','2003-12-27T11:57:53',('Lothar Klein'),('LKSoft'),' ','IDA-STEP',' ');
        FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 2 1 1}'));
        ENDSEC;
        DATA;
        #10=ORGANIZATION('O0001','LKSoft','company');
        #11=PRODUCT_DEFINITION_CONTEXT('part definition',#12,'manufacturing');
        #12=APPLICATION_CONTEXT('mechanical design');
        #13=APPLICATION_PROTOCOL_DEFINITION('','automotive_design',2003,#12);
        #14=PRODUCT_DEFINITION('0',$,#15,#11);
        #15=PRODUCT_DEFINITION_FORMATION('1',$,#16);
        #16=PRODUCT('A0001','Test Part 1','',(#18));
        #17=PRODUCT_RELATED_PRODUCT_CATEGORY('part',$,(#16));
        #18=PRODUCT_CONTEXT('',#12,'');
        #19=APPLIED_ORGANIZATION_ASSIGNMENT(#10,#20,(#16));
        #20=ORGANIZATION_ROLE('id owner');
        ENDSEC;
        END-ISO-10303-21;
        """;

    /// <summary>Header only; DATA body is substituted per-test. Keeps fixtures to one line of intent.</summary>
    public static string WithData(string dataBody, string schema = "TEST") => $"""
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('t'),'2;1');
        FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
        FILE_SCHEMA(('{schema}'));
        ENDSEC;
        DATA;
        {dataBody}
        ENDSEC;
        END-ISO-10303-21;
        """;

    /// <summary>Every <see cref="StepParser.Parser.Parameter"/> variant that the grammar can produce.</summary>
    public const string AllParameterKinds = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('t'),'2;1');
        FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
        FILE_SCHEMA(('TEST'));
        ENDSEC;
        DATA;
        #1=EVERYTHING('text',42,3.5,-1.25E-3,.ENUM.,"00FF",$,*,#2,(1,2),LENGTH_MEASURE(9.5));
        #2=TARGET('x');
        ENDSEC;
        END-ISO-10303-21;
        """;

    /// <summary>AP242 file carrying PMI dimensions, a datum and GD&amp;T, for the semantic layer.</summary>
    public const string Ap242Pmi = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('pmi'),'2;1');
        FILE_NAME('pmi','2024',('a'),('b'),' ','s',' ');
        FILE_SCHEMA(('AP242_MANAGED_MODEL_BASED_3D_ENGINEERING_MIM_LF { 1 0 10303 442 1 1 4 }'));
        ENDSEC;
        DATA;
        #1=ADVANCED_FACE('face',(),#20,.T.);
        #2=DIMENSIONAL_SIZE(#10,'width');
        #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#30);
        #4=DATUM('A','primary datum',#10);
        #5=FLATNESS_TOLERANCE('flat','',#40,#10);
        #6=GEOMETRIC_REPRESENTATION_CONTEXT(3);
        #10=SHAPE_ASPECT('aspect','',#6,.T.);
        #20=PLANE('p',#21);
        #21=AXIS2_PLACEMENT_3D('',#22,$,$);
        #22=CARTESIAN_POINT('',(0.,0.,0.));
        #30=LENGTH_UNIT();
        #40=LENGTH_MEASURE_WITH_UNIT(LENGTH_MEASURE(0.1),#30);
        ENDSEC;
        END-ISO-10303-21;
        """;
}
