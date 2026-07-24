module Fuaran.UI.Ops.CleanRoom.Tests.SkeletonTests

// ============================================================================
//  Acceptance: the projection emits a content-free skeleton, and count /
//  length descriptors are coarsened (not exact). The no-content-survives test
//  is the structural analogue of the renderer's no-raw-literal test.
// ============================================================================

open Expecto
open Fuaran.UI
open Fuaran.UI.Types
open Fuaran.UI.Ops.CleanRoom
open Fuaran.UI.Ops.CleanRoom.Tests.Fixtures

/// Collect every string that appears anywhere in a skeleton value: ids + kind
/// discriminators. (The bounded descriptors carry no strings.) `%A` over the
/// whole value is the broad net — if any content sentinel were copied in, it
/// would show up here.
let private renderedSkeleton (sk: Skeleton.Skeleton) : string = sprintf "%A" sk

[<Tests>]
let tests =
    testList
        "structure-only clean room — skeleton projection"
        [ test "no leaf content string survives into a Skeleton" {
              let sk = Skeleton.project realTree
              let rendered = renderedSkeleton sk

              for secret in [ secretHeadingA; secretHeadingB; secretBody; secretMetric ] do
                  Expect.isFalse
                      (rendered.Contains secret)
                      (sprintf "content sentinel '%s' must not survive into the skeleton" secret)
          }

          test "skeleton preserves ids + structural kinds (the addressing surface)" {
              let sk = Skeleton.project realTree
              Expect.equal sk.Root.Id (NodeId "doc-root") "root id preserved"
              Expect.equal sk.Root.Kind "Box" "root structural kind preserved"

              let ids = Skeleton.nodeIds sk |> List.map (fun (NodeId s) -> s) |> Set.ofList

              Expect.isTrue
                  (Set.isSubset (Set.ofList [ "doc-root"; "clause-1"; "clause-2"; "recital"; "headline-metric" ]) ids)
                  "every authored id is addressable in the skeleton"
          }

          test "child-count descriptors are coarsened, not exact" {
              // Two containers whose exact child counts differ but fall in the
              // same bucket must project to the same ChildCount descriptor.
              let stackOf n =
                  Fuaran.stack
                      "s"
                      { Defaults.stack<Msg> with
                          Children = [ for i in 1..n -> Fuaran.markdown (sprintf "k%d" i) "x" ] }

              let descOf n =
                  (Skeleton.project (stackOf n)).Root.Descriptor.ChildCount

              // 6 and 10 children both fall in the 5–12 "Several" bucket.
              Expect.equal (descOf 6) Skeleton.CountBucket.Several "6 children → Several"
              Expect.equal (descOf 10) Skeleton.CountBucket.Several "10 children → Several"
              Expect.equal (descOf 6) (descOf 10) "differing exact counts in one bucket are indistinguishable"
              // A 1-child container is distinguishable from a many-child one
              // (coarse, but not degenerate).
              Expect.equal (descOf 1) Skeleton.CountBucket.One "1 child → One"
              Expect.notEqual (descOf 1) (descOf 6) "buckets still separate coarse magnitudes"
          }

          test "content-length descriptors are coarsened, not exact" {
              let mdOfLength len =
                  Fuaran.markdown "m" (System.String('x', len))

              let lenOf len =
                  (Skeleton.project (mdOfLength len)).Root.Descriptor.ContentLength

              // 200 and 900 chars both fall in the 129–1024 "Long" bucket.
              Expect.equal (lenOf 200) Skeleton.LengthBucket.Long "200 chars → Long"
              Expect.equal (lenOf 900) Skeleton.LengthBucket.Long "900 chars → Long"
              Expect.equal (lenOf 200) (lenOf 900) "differing exact lengths in one bucket are indistinguishable"
              Expect.equal (lenOf 0) Skeleton.LengthBucket.Empty "0 chars → Empty"
          }

          test "default descriptor classifies structural role from kind alone" {
              let sk = Skeleton.project realTree
              Expect.equal sk.Root.Descriptor.Role Skeleton.StructuralRole.Container "dashboard → Container"

              let roleOfId id =
                  Skeleton.nodeIds sk |> ignore

                  let rec find (n: Skeleton.SkeletonNode) =
                      if n.Id = NodeId id then
                          Some n.Descriptor.Role
                      else
                          n.Children |> List.tryPick find

                  find sk.Root

              Expect.equal (roleOfId "clause-1-title") (Some Skeleton.StructuralRole.Heading) "heading → Heading"
              Expect.equal (roleOfId "recital") (Some Skeleton.StructuralRole.TextBlock) "markdown → TextBlock"
              Expect.equal (roleOfId "headline-metric") (Some Skeleton.StructuralRole.DataView) "metric → DataView"
          } ]
