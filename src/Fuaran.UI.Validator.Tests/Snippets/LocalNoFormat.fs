module Snippets.LocalNoFormat

open Fuaran.UI
open Fuaran.UI.Types

type Msg = SetSalary of string

let build () : Node<Msg> =
    Fuaran.form
        "f"
        { Defaults.form<Msg> with
            Fields =
                [ { Defaults.formField<Msg> with
                      Id = "salary"
                      Kind =
                          FormFieldKind.Text(
                              Some(
                                  binding.local
                                      (binding.state "salary" "0")
                                      LocalFlushTrigger.OnBlur
                                      (fun s -> Action.dispatch (SetSalary s))
                                      None
                                      (fun s -> Ok s)
                              ),
                              None
                          ) } ] }
