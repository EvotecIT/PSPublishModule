Task: For every cmdlet class (PSCmdlet/Cmdlet) in this solution, add/normalize XML doc comments to generate PowerShell help with MatejKafka.XmlDoc2CmdletDoc.
Rules:

Each cmdlet must have exactly one <summary> (single sentence synopsis) and one or more <para> elements (description).
Each public [Parameter] property/field must have a description: either plain text or one/more <para>.
For complex input/output types used by parameters or returned objects, ensure their types have a short doc summary or <para> description.
Add a <list type="alertSet"> with at least one note about behavior/side-effects if relevant.
Provide at least two <example> blocks per cmdlet, each with <summary>, <prefix>PS&gt; </prefix>, <code> showing the invocation, and one or more <para> remarks.
Add two <seealso> links per cmdlet: one to relevant MS Learn and one to project docs, using the href attribute.
Preserve existing behavior; do not modify code outside of comments.
Ensure the project has <GenerateDocumentationFile>true</GenerateDocumentationFile> and a PackageReference to MatejKafka.XmlDoc2CmdletDoc.
If any cmdlet still lacks a synopsis/description, fail the build via -strict in <XmlDoc2CmdletDocArguments>.
Output: Commit the updated code comments; do not touch build logic beyond the property/package reference.