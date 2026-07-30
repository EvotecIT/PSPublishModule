Task: For every cmdlet class (PSCmdlet/Cmdlet) in this solution, add or normalize XML doc comments so PowerForge can generate command Markdown and external PowerShell help.
Rules:

Each cmdlet must have exactly one <summary> (single sentence synopsis) and one or more <para> elements (description).
Each public [Parameter] property/field must have a description: either plain text or one/more <para>.
For complex input/output types used by parameters or returned objects, ensure their types have a short doc summary or <para> description.
Add a <list type="alertSet"> with at least one note about behavior/side-effects if relevant.
Provide at least two <example> blocks per cmdlet, each with <summary>, <prefix>PS&gt; </prefix>, <code> showing the invocation, and one or more <para> remarks.
Add two <seealso> links per cmdlet: one to relevant MS Learn and one to project docs, using the href attribute.
Preserve existing behavior; outside comments, make only the minimal project and PowerForge build-configuration edits described below.
Ensure the binary cmdlet project has <GenerateDocumentationFile>true</GenerateDocumentationFile>.
Configure the module build with New-ConfigurationDocumentation -Enable -Path 'Docs' -PathReadme 'Docs\Readme.md' and New-ConfigurationBuild -NETBinaryModuleDocumentation.
If migrating from MatejKafka.XmlDoc2CmdletDoc, remove its package reference, MSBuild properties, and package-specific copy targets.
When complete documentation is a build requirement, use New-ConfigurationValidation -Enable -DocumentationSeverity Error -MinSynopsisPercent 100 -MinParameterDescriptionPercent 100 -MinTypeDescriptionPercent 100 -MinExamplesPerCommand 2.
Output: Commit the updated code comments and the minimal PowerForge build configuration required to generate and validate the documentation.
