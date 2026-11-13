# Phase 2692 - the DAG merge over Core's reachability index

Host: Intel Core i7-8650U (8 logical processors), Windows 11 (10.0.26300), .NET SDK 10.0.203, Release. The host was
not idle (sibling sessions building), so wall times moved up to 2x between runs; allocation repeats to within a few
bytes and is the figure to read. The tables and their reading are in `docs/DAG_STRATEGY.md` ("The merge over Core's
reachability index, measured"); this file keeps the raw output.

## `merge-rate 3 1000,5000,20000` before the change (tree `2d559670`, attribution lines added)

```
merge.lca.1000.mean_ms = 71.673
merge.lca.1000.alloc_b = 6113261
merge.sink.1000.mean_ms = 180.549
merge.sink.1000.alloc_b = 56856128
merge.sink_ckpt.1000.mean_ms = 156.273
merge.sink_ckpt.1000.alloc_b = 43400755
merge.lookups_ckpt.1000.mean_ms = 103.644
merge.lookups_ckpt.1000.alloc_b = 32930328
merge.callee.load.1000.mean_ms = 30.494
merge.callee.load.1000.alloc_b = 10494317
merge.callee.mergeBase.1000.mean_ms = 21.591
merge.callee.mergeBase.1000.alloc_b = 6113317
merge.callee.betweenOps.1000.mean_ms = 12.935
merge.callee.betweenOps.1000.alloc_b = 4255701
merge.callee.ancestorsOf.1000.mean_ms = 1.284
merge.callee.ancestorsOf.1000.alloc_b = 520845
merge.callee.verifyCheckpoint.1000.mean_ms = 0.268
merge.callee.verifyCheckpoint.1000.alloc_b = 173149
merge.callee.replayTo.1000.mean_ms = 20.318
merge.callee.replayTo.1000.alloc_b = 7694141
merge.callee.replayFrom.1000.mean_ms = 18.222
merge.callee.replayFrom.1000.alloc_b = 4379821
merge.callee.mint.1000.mean_ms = 0.033
merge.callee.mint.1000.alloc_b = 8925
merge.callee.reach.ofDag.1000.mean_ms = 17.238
merge.callee.reach.ofDag.1000.alloc_b = 3554405
merge.callee.reach.mergeBase.1000.mean_ms = 0.056
merge.callee.reach.mergeBase.1000.alloc_b = 245
merge.callee.reach.between.1000.mean_ms = 0.142
merge.callee.reach.between.1000.alloc_b = 949
merge.callee.reach.ancestors.1000.mean_ms = 2.454
merge.callee.reach.ancestors.1000.alloc_b = 472189
merge.callee.reach.replayTo.1000.mean_ms = 6.484
merge.callee.reach.replayTo.1000.alloc_b = 3979573
merge.callee.reach.replayFrom.1000.mean_ms = 2.862
merge.callee.reach.replayFrom.1000.alloc_b = 624637
merge.callee.reach.extend.1000.mean_ms = 0.131
merge.callee.reach.extend.1000.alloc_b = 39005
merge.lca.5000.mean_ms = 177.416
merge.lca.5000.alloc_b = 36808725
merge.sink.5000.mean_ms = 1607.063
merge.sink.5000.alloc_b = 309665216
merge.sink_ckpt.5000.mean_ms = 1679.881
merge.sink_ckpt.5000.alloc_b = 244096195
merge.lookups_ckpt.5000.mean_ms = 901.761
merge.lookups_ckpt.5000.alloc_b = 190590171
merge.callee.load.5000.mean_ms = 237.455
merge.callee.load.5000.alloc_b = 53505613
merge.callee.mergeBase.5000.mean_ms = 188.151
merge.callee.mergeBase.5000.alloc_b = 36808661
merge.callee.betweenOps.5000.mean_ms = 281.096
merge.callee.betweenOps.5000.alloc_b = 25402243
merge.callee.ancestorsOf.5000.mean_ms = 21.626
merge.callee.ancestorsOf.5000.alloc_b = 3161901
merge.callee.verifyCheckpoint.5000.mean_ms = 0.258
merge.callee.verifyCheckpoint.5000.alloc_b = 170813
merge.callee.replayTo.5000.mean_ms = 152.744
merge.callee.replayTo.5000.alloc_b = 41874997
merge.callee.replayFrom.5000.mean_ms = 121.913
merge.callee.replayFrom.5000.alloc_b = 25525189
merge.callee.mint.5000.mean_ms = 0.037
merge.callee.mint.5000.alloc_b = 8941
merge.callee.reach.ofDag.5000.mean_ms = 168.512
merge.callee.reach.ofDag.5000.alloc_b = 21628325
merge.callee.reach.mergeBase.5000.mean_ms = 0.918
merge.callee.reach.mergeBase.5000.alloc_b = 149
merge.callee.reach.between.5000.mean_ms = 0.296
merge.callee.reach.between.5000.alloc_b = 949
merge.callee.reach.ancestors.5000.mean_ms = 33.503
merge.callee.reach.ancestors.5000.alloc_b = 2951093
merge.callee.reach.replayTo.5000.mean_ms = 93.628
merge.callee.reach.replayTo.5000.alloc_b = 19757845
merge.callee.reach.replayFrom.5000.mean_ms = 42.774
merge.callee.reach.replayFrom.5000.alloc_b = 3228901
merge.callee.reach.extend.5000.mean_ms = 21.913
merge.callee.reach.extend.5000.alloc_b = 151541
merge.lca.20000.mean_ms = 1329.824
merge.lca.20000.alloc_b = 168096509
merge.sink.20000.mean_ms = 6882.469
merge.sink.20000.alloc_b = 1339045323
merge.sink_ckpt.20000.mean_ms = 5750.152
merge.sink_ckpt.20000.alloc_b = 1084364157
merge.lookups_ckpt.20000.mean_ms = 4342.268
merge.lookups_ckpt.20000.alloc_b = 864469792
merge.callee.load.20000.mean_ms = 588.508
merge.callee.load.20000.alloc_b = 219894037
merge.callee.mergeBase.20000.mean_ms = 558.968
merge.callee.mergeBase.20000.alloc_b = 168096533
merge.callee.betweenOps.20000.mean_ms = 390.534
merge.callee.betweenOps.20000.alloc_b = 115768173
merge.callee.ancestorsOf.20000.mean_ms = 51.679
merge.callee.ancestorsOf.20000.alloc_b = 14590893
merge.callee.verifyCheckpoint.20000.mean_ms = 0.245
merge.callee.verifyCheckpoint.20000.alloc_b = 170781
merge.callee.replayTo.20000.mean_ms = 671.499
merge.callee.replayTo.20000.alloc_b = 179518557
merge.callee.replayFrom.20000.mean_ms = 507.208
merge.callee.replayFrom.20000.alloc_b = 115891045
merge.callee.mint.20000.mean_ms = 0.072
merge.callee.mint.20000.alloc_b = 9165
merge.callee.reach.ofDag.20000.mean_ms = 599.336
merge.callee.reach.ofDag.20000.alloc_b = 114982811
merge.callee.reach.mergeBase.20000.mean_ms = 0.277
merge.callee.reach.mergeBase.20000.alloc_b = 149
merge.callee.reach.between.20000.mean_ms = 0.577
merge.callee.reach.between.20000.alloc_b = 853
merge.callee.reach.ancestors.20000.mean_ms = 71.949
merge.callee.reach.ancestors.20000.alloc_b = 13720005
merge.callee.reach.replayTo.20000.mean_ms = 210.117
merge.callee.reach.replayTo.20000.alloc_b = 78928109
merge.callee.reach.replayFrom.20000.mean_ms = 82.879
merge.callee.reach.replayFrom.20000.alloc_b = 14477269
merge.callee.reach.extend.20000.mean_ms = 0.514
merge.callee.reach.extend.20000.alloc_b = 573829
```

## `merge-rate 3 1000,5000,20000` after the change

```
merge.lca.1000.mean_ms = 59.262
merge.lca.1000.alloc_b = 6113261
merge.sink.1000.mean_ms = 197.511
merge.sink.1000.alloc_b = 56856632
merge.sink_ckpt.1000.mean_ms = 149.891
merge.sink_ckpt.1000.alloc_b = 43429531
merge.lookups_ckpt.1000.mean_ms = 101.946
merge.lookups_ckpt.1000.alloc_b = 32930755
merge.indexed.1000.mean_ms = 38.006
merge.indexed.1000.alloc_b = 16894133
merge.indexed_ckpt.1000.mean_ms = 20.883
merge.indexed_ckpt.1000.alloc_b = 3302045
merge.callee.load.1000.mean_ms = 37.147
merge.callee.load.1000.alloc_b = 10494317
merge.callee.mergeBase.1000.mean_ms = 18.936
merge.callee.mergeBase.1000.alloc_b = 6113221
merge.callee.betweenOps.1000.mean_ms = 14.262
merge.callee.betweenOps.1000.alloc_b = 4255701
merge.callee.ancestorsOf.1000.mean_ms = 1.254
merge.callee.ancestorsOf.1000.alloc_b = 520813
merge.callee.verifyCheckpoint.1000.mean_ms = 0.376
merge.callee.verifyCheckpoint.1000.alloc_b = 173085
merge.callee.replayTo.1000.mean_ms = 18.003
merge.callee.replayTo.1000.alloc_b = 7694141
merge.callee.replayFrom.1000.mean_ms = 12.142
merge.callee.replayFrom.1000.alloc_b = 4379757
merge.callee.mint.1000.mean_ms = 0.034
merge.callee.mint.1000.alloc_b = 8925
merge.callee.reach.ofDag.1000.mean_ms = 22.111
merge.callee.reach.ofDag.1000.alloc_b = 3554405
merge.callee.reach.mergeBase.1000.mean_ms = 0.857
merge.callee.reach.mergeBase.1000.alloc_b = 245
merge.callee.reach.between.1000.mean_ms = 0.493
merge.callee.reach.between.1000.alloc_b = 949
merge.callee.reach.ancestors.1000.mean_ms = 0.864
merge.callee.reach.ancestors.1000.alloc_b = 472189
merge.callee.reach.replayTo.1000.mean_ms = 6.511
merge.callee.reach.replayTo.1000.alloc_b = 3979573
merge.callee.reach.replayFrom.1000.mean_ms = 4.563
merge.callee.reach.replayFrom.1000.alloc_b = 624605
merge.callee.reach.extend.1000.mean_ms = 2.201
merge.callee.reach.extend.1000.alloc_b = 39005
merge.lca.5000.mean_ms = 146.874
merge.lca.5000.alloc_b = 36808821
merge.sink.5000.mean_ms = 1101.867
merge.sink.5000.alloc_b = 309666368
merge.sink_ckpt.5000.mean_ms = 1022.696
merge.sink_ckpt.5000.alloc_b = 244096707
merge.lookups_ckpt.5000.mean_ms = 916.044
merge.lookups_ckpt.5000.alloc_b = 190591141
merge.indexed.5000.mean_ms = 183.346
merge.indexed.5000.alloc_b = 80112469
merge.indexed_ckpt.5000.mean_ms = 53.831
merge.indexed_ckpt.5000.alloc_b = 13827309
merge.callee.load.5000.mean_ms = 150.865
merge.callee.load.5000.alloc_b = 53505613
merge.callee.mergeBase.5000.mean_ms = 156.507
merge.callee.mergeBase.5000.alloc_b = 36808771
merge.callee.betweenOps.5000.mean_ms = 101.658
merge.callee.betweenOps.5000.alloc_b = 25402221
merge.callee.ancestorsOf.5000.mean_ms = 16.581
merge.callee.ancestorsOf.5000.alloc_b = 3161901
merge.callee.verifyCheckpoint.5000.mean_ms = 0.275
merge.callee.verifyCheckpoint.5000.alloc_b = 170813
merge.callee.replayTo.5000.mean_ms = 123.705
merge.callee.replayTo.5000.alloc_b = 41874997
merge.callee.replayFrom.5000.mean_ms = 103.960
merge.callee.replayFrom.5000.alloc_b = 25525125
merge.callee.mint.5000.mean_ms = 0.032
merge.callee.mint.5000.alloc_b = 8941
merge.callee.reach.ofDag.5000.mean_ms = 110.225
merge.callee.reach.ofDag.5000.alloc_b = 21628325
merge.callee.reach.mergeBase.5000.mean_ms = 0.108
merge.callee.reach.mergeBase.5000.alloc_b = 245
merge.callee.reach.between.5000.mean_ms = 0.065
merge.callee.reach.between.5000.alloc_b = 949
merge.callee.reach.ancestors.5000.mean_ms = 20.713
merge.callee.reach.ancestors.5000.alloc_b = 2951093
merge.callee.reach.replayTo.5000.mean_ms = 56.967
merge.callee.reach.replayTo.5000.alloc_b = 19757845
merge.callee.reach.replayFrom.5000.mean_ms = 21.273
merge.callee.reach.replayFrom.5000.alloc_b = 3228901
merge.callee.reach.extend.5000.mean_ms = 0.165
merge.callee.reach.extend.5000.alloc_b = 151541
merge.lca.20000.mean_ms = 1019.353
merge.lca.20000.alloc_b = 168097323
merge.sink.20000.mean_ms = 6215.771
merge.sink.20000.alloc_b = 1339044344
merge.sink_ckpt.20000.mean_ms = 6092.285
merge.sink_ckpt.20000.alloc_b = 1084364816
merge.lookups_ckpt.20000.mean_ms = 8885.338
merge.lookups_ckpt.20000.alloc_b = 864476800
merge.indexed.20000.mean_ms = 1134.464
merge.indexed.20000.alloc_b = 317219573
merge.indexed_ckpt.20000.mean_ms = 741.905
merge.indexed_ckpt.20000.alloc_b = 59246875
merge.callee.load.20000.mean_ms = 1134.317
merge.callee.load.20000.alloc_b = 219894037
merge.callee.mergeBase.20000.mean_ms = 1982.127
merge.callee.mergeBase.20000.alloc_b = 168098499
merge.callee.betweenOps.20000.mean_ms = 699.692
merge.callee.betweenOps.20000.alloc_b = 115768283
merge.callee.ancestorsOf.20000.mean_ms = 98.220
merge.callee.ancestorsOf.20000.alloc_b = 14590893
merge.callee.verifyCheckpoint.20000.mean_ms = 0.405
merge.callee.verifyCheckpoint.20000.alloc_b = 170781
merge.callee.replayTo.20000.mean_ms = 1195.363
merge.callee.replayTo.20000.alloc_b = 179518659
merge.callee.replayFrom.20000.mean_ms = 701.140
merge.callee.replayFrom.20000.alloc_b = 115893403
merge.callee.mint.20000.mean_ms = 0.036
merge.callee.mint.20000.alloc_b = 9165
merge.callee.reach.ofDag.20000.mean_ms = 870.747
merge.callee.reach.ofDag.20000.alloc_b = 114981973
merge.callee.reach.mergeBase.20000.mean_ms = 0.263
merge.callee.reach.mergeBase.20000.alloc_b = 245
merge.callee.reach.between.20000.mean_ms = 0.360
merge.callee.reach.between.20000.alloc_b = 949
merge.callee.reach.ancestors.20000.mean_ms = 62.869
merge.callee.reach.ancestors.20000.alloc_b = 13720005
merge.callee.reach.replayTo.20000.mean_ms = 177.704
merge.callee.reach.replayTo.20000.alloc_b = 78928085
merge.callee.reach.replayFrom.20000.mean_ms = 77.138
merge.callee.reach.replayFrom.20000.alloc_b = 14477232
merge.callee.reach.extend.20000.mean_ms = 0.543
merge.callee.reach.extend.20000.alloc_b = 573816
```

## `merge-bound 10`

```
merge.bound.indexed_ckpt.1000.mean_ms = 13.271
merge.bound.indexed_ckpt.1000.alloc_b = 3305636
merge.bound.indexed_ckpt.20000.mean_ms = 322.547
merge.bound.indexed_ckpt.20000.alloc_b = 59247305
merge.bound.wall_ratio = 24.30 (reported, not pinned)
merge.bound.alloc_ratio = 17.92 (pinned at <= 20.0)
merge-bound: PASS
```

## `merge-snapshot`

838 lines, sha256 `d4cef8c8e752b40992d7ddb066e2c6be36b0bbc01b570729abcbccda3900b79a`: byte-identical to the Phase 2648
baseline, so no merge result, address or tree moved.
