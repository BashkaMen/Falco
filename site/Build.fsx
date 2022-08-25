#I __SOURCE_DIRECTORY__

#r "nuget: Markdig"
#r "nuget: Scriban"

open System
open System.IO
open System.Linq
open System.Text.RegularExpressions
open Markdig
open Markdig.Extensions.Yaml
open Markdig.Renderers
open Markdig.Syntax
open Scriban

module Log =
    let private log kind fmt =
        Printf.kprintf (fun s ->
            let now = DateTime.Now
            let msg = sprintf "[%s] [%s] %s" (now.ToString("s")) kind s

            printfn "%s" msg) fmt

    let fail fmt = log "Fail" fmt

    let info fmt = log "Info" fmt

module Path =
    let resolve (childPath : string) =
        Path.Join(__SOURCE_DIRECTORY__, childPath)

module Directory =
    let copyRecursive (destinationDir : string) (sourceDir : string) =
        let rec copy destDir srcDir =
            let srcDirInfo = DirectoryInfo(srcDir)

            for subDir in srcDirInfo.GetDirectories() do
                copy (Path.Join(destDir, subDir.Name)) subDir.FullName

            for file in srcDirInfo.GetFiles() do
                if not(Directory.Exists(destDir)) then
                    Directory.CreateDirectory(destDir) |> ignore

                File.Copy(file.FullName, Path.Join(destDir, file.Name))

        copy destinationDir sourceDir


type ParsedMarkdownDocument =
    { Title : string
      Body  : string }

module Markdown =
    let render (markdown : string) =
        let pipeline =
            MarkdownPipelineBuilder()
                .UseAutoIdentifiers()
                .UsePipeTables()
                .UseYamlFrontMatter()
                .Build()

        use sw = new StringWriter()
        let renderer = HtmlRenderer(sw)

        pipeline.Setup(renderer) |> ignore

        let doc = Markdown.Parse(markdown, pipeline)

        renderer.Render(doc) |> ignore
        sw.Flush() |> ignore
        let renderedMarkdown = sw.ToString()

        let frontmatter =
            match doc.Descendants<YamlFrontMatterBlock>().FirstOrDefault() with
            | null -> Map.empty
            | yamlBlock ->
                markdown
                    .Substring(yamlBlock.Span.Start, yamlBlock.Span.End)
                    .Split("\r")
                |> Array.tail
                |> Array.rev
                |> Array.tail
                |> Array.map (fun x ->
                    let keyValue = x.Split(":")
                    keyValue.[0].Trim(), keyValue.[1].Trim())
                |> Map.ofArray

        // rewrite markdown links
        let body = Regex.Replace(renderedMarkdown, "([a-zA-Z\-]+)\.md", "$1.html")

        { Title = Map.tryFind "title" frontmatter |> Option.defaultValue ""
          Body  = body }

    let renderFile (path : string) =
        render (File.ReadAllText(path))

type LayoutTemplate =
    { Title : string
      MainContent : string
      CopyrightYear : int }

type LayoutTwoColTemplate =
    { Title : string
      SideContent : string
      MainContent : string
      CopyrightYear : int }

module Template =
    let private layoutTmpl =
        Path.resolve "templates/layout.html"
        |> File.ReadAllText
        |> Template.Parse

    let layout (template : LayoutTemplate) =
        layoutTmpl.Render(template)

    let private layoutTwoColTmpl =
        Path.resolve "templates/layout-twocol.html"
        |> File.ReadAllText
        |> Template.Parse

    let layoutTwoCol (template : LayoutTwoColTemplate) =
        layoutTwoColTmpl.Render(template)

    module Partials =
        let docsLinks =
            Path.resolve "templates/partials/docs-links.html"
            |> File.ReadAllText
            |> Template.Parse
            |> fun t -> t.Render()

        let docsNav =
            Path.resolve "templates/partials/docs-nav.html"
            |> File.ReadAllText
            |> Template.Parse
            |> fun t -> t.Render()

//
// Build site

// Clean build
let buildDir = Path.resolve "build"

Log.info "Clearing build directory..."
if Directory.Exists(buildDir) then Directory.Delete(buildDir, true)
Directory.CreateDirectory(buildDir)

// Copy asset
let assetsDir = Path.resolve "assets"

Log.info "Copying assets..."
Directory.copyRecursive buildDir assetsDir

// Render homepage
let homepageFile = Path.resolve "../README.md"
let mainContent = Markdown.renderFile homepageFile

Log.info "Rendering homepage..."

{ Title = String.Empty
  MainContent = mainContent.Body
  CopyrightYear = DateTime.Now.Year }
|> Template.layout
|> fun text -> File.WriteAllText(Path.Join(buildDir, "index.html"), text)

// Render docs
let docsDir = Path.resolve "../docs"
let docsBuildDir = Path.Join(buildDir, "docs")
Directory.CreateDirectory(docsBuildDir)

let buildDocs (docs : FileInfo[]) (buildDir : string) =
    if not(Directory.Exists(buildDir)) then
        Directory.CreateDirectory(buildDir) |> ignore

    for file in docs do
        let buildFilename, sideContent =
            if file.Name = "readme.md" then
                "index.html", Template.Partials.docsLinks
            else
                Path.ChangeExtension(file.Name, ".html"), Template.Partials.docsNav

        let mainContent = Markdown.renderFile file.FullName

        let html =
            { Title = mainContent.Title
              SideContent = sideContent
              MainContent = mainContent.Body
              CopyrightYear = DateTime.Now.Year }
            |> Template.layoutTwoCol

        File.WriteAllText(Path.Join(buildDir, buildFilename), html)

Log.info "Rendering docs..."
buildDocs (DirectoryInfo(docsDir).GetFiles()) docsBuildDir

// Additional languages
let languageCodes = []

for languageCode in languageCodes do
    Log.info "Rendering /%s docs" languageCode
    let languageDir = DirectoryInfo(Path.Join(docsDir, languageCode))
    let languageBuildDir = Path.Join(docsBuildDir, languageCode)
    buildDocs (languageDir.GetFiles()) languageBuildDir