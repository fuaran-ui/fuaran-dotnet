module Fuaran.UI.Renderer.Server.Tests.MarkupTests

// ============================================================================
//  Phase 2515 — the server's own string emitter, pinned by bytes.
//
//  `Markup` replaced Feliz.ViewEngine as the thing that writes the server's
//  HTML. The parity, hydration and email corpora pin the documents it writes;
//  these pin the emitter's own rules, one per rule, with the exact bytes the
//  library it replaced wrote, so a change to the escaping floor or to an
//  element's shape fails here by name rather than as a corpus diff somewhere
//  downstream.
// ============================================================================

open Expecto
open Fuaran.UI.Renderer
open Fuaran.UI.Renderer.Server

let private html = Markup.toHtml

let private planElement tag props content : ElementPlan.Plan<Markup> =
    ElementPlan.Plan.element tag props content

[<Tests>]
let tests =
    testList
        "Fuaran.UI.Renderer.Server — Markup, the string emitter (Phase 2515)"
        [ test "the escaping floor escapes exactly five characters, apostrophe as &apos;" {
              Expect.equal
                  (Markup.escape "<a href=\"x\">Tom's & co</a>")
                  "&lt;a href=&quot;x&quot;&gt;Tom&apos;s &amp; co&lt;/a&gt;"
                  "the five"

              Expect.equal (Markup.escape "plain — text é") "plain — text é" "everything else passes"
              Expect.equal (Markup.escape null) "" "a null escapes to nothing"
          }

          test "text content and attribute values are escaped; names and order are kept" {
              let m = Markup.textElement "div" [ "class", "a\"b"; "data-x", "1<2" ] "x & y"

              Expect.equal (html m) "<div class=\"a&quot;b\" data-x=\"1&lt;2\">x &amp; y</div>" "escaped, in order"
          }

          test "an element with no attributes and no content is an open and a close tag" {
              Expect.equal (html (Markup.element "span" [] [])) "<span></span>" "bare"
          }

          test "a void element has no closing tag and no self-closing slash" {
              Expect.equal
                  (html (Markup.voidElement "img" [ "src", "a.png"; "alt", "" ]))
                  "<img src=\"a.png\" alt=\"\">"
                  "void"

              Expect.equal (html (Markup.voidElement "br" [])) "<br>" "bare void"
          }

          test "raw markup is written verbatim, escaped text is not markup" {
              Expect.equal
                  (html (Markup.rawElement "style" [ "nonce", "n'1" ] "a>b{}"))
                  "<style nonce=\"n&apos;1\">a>b{}</style>"
                  "raw body"

              Expect.equal (html (Markup.raw "<b>x</b>")) "<b>x</b>" "raw"
              Expect.equal (html (Markup.text "<b>x</b>")) "&lt;b&gt;x&lt;/b&gt;" "text"
          }

          test "fragments concatenate in order, and empty writes nothing" {
              let m =
                  Markup.concat [ Markup.text "a"; Markup.empty; Markup.element "i" [] [ Markup.text "b" ] ]

              Expect.equal (html m) "a<i>b</i>" "in order"
          }

          test "a plan's flags keep both of the spellings the server served" {
              let p =
                  planElement
                      "button"
                      [ ElementPlan.Prop.Flag "disabled"; ElementPlan.Prop.Flag "hidden" ]
                      ElementPlan.Content.Empty

              Expect.equal
                  (html (Markup.ofPlan p))
                  "<button disabled=\"true\" hidden=\"\"></button>"
                  "library boolean, then custom"
          }

          test "a plan's style declarations are one escaped style attribute" {
              let p =
                  planElement
                      "div"
                      [ ElementPlan.Prop.Style [ "width", "50%"; "font-family", "'A'" ] ]
                      ElementPlan.Content.Empty

              Expect.equal (html (Markup.ofPlan p)) "<div style=\"width:50%;font-family:&apos;A&apos;\"></div>" "joined"
          }

          test "a controlled value lands where the document shows it" {
              let input =
                  planElement "input" [ ElementPlan.Prop.Value "v" ] (ElementPlan.Content.Text "dropped")

              let textarea =
                  planElement "textarea" [ ElementPlan.Prop.Value "a<b" ] ElementPlan.Content.Empty

              let select =
                  planElement
                      "select"
                      [ ElementPlan.Prop.Value "x"; ElementPlan.Prop.Values [ "x" ] ]
                      (ElementPlan.Content.Children
                          [ planElement "option" [ ElementPlan.Prop.Selected ] (ElementPlan.Content.Text "X") ])

              Expect.equal
                  (html (Markup.ofPlan input))
                  "<input value=\"v\">"
                  "an input's attribute; a void tag takes no content"

              Expect.equal (html (Markup.ofPlan textarea)) "<textarea>a&lt;b</textarea>" "a textarea's text"

              Expect.equal
                  (html (Markup.ofPlan select))
                  "<select><option selected=\"selected\">X</option></select>"
                  "a select's options"
          }

          test "a plan's behaviour points and keys write nothing; inner HTML is raw" {
              let p =
                  ElementPlan.Plan.Element
                      { Tag = "div"
                        Props =
                          [ ElementPlan.Prop.Behaviour("tab-select", "1")
                            ElementPlan.Prop.Attr("class", "c") ]
                        Key = Some "k"
                        Content = ElementPlan.Content.InnerHtml "<svg></svg>" }

              Expect.equal (html (Markup.ofPlan p)) "<div class=\"c\"><svg></svg></div>" "inert, unkeyed, raw"
          }

          test "a host element rides a plan unchanged, and Nothing is nothing" {
              let p =
                  ElementPlan.Plan.classChildren
                      "p"
                      "x"
                      [ ElementPlan.Plan.Host(Markup.raw "<b>h</b>")
                        ElementPlan.Plan.Nothing
                        ElementPlan.Plan.TextNode "t&" ]

              Expect.equal (html (Markup.ofPlan p)) "<p class=\"x\"><b>h</b>t&amp;</p>" "host, nothing, text"
          } ]
