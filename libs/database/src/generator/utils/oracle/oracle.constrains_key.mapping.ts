import {
  OracleConstrains,
  OracleConstrainType,
} from '@hsm-lib/database/generator/definitions';

type OraclePkConstrain = Map<string, string[]>;
type OracleFkConstraint = Map<
  string,
  {
    columns: string[];
    referencedOwner: string | null;
    referencedConstraintName: string | null;
    referencedTable: string | null;
  }
>;
type OracleUkConstrain = Map<string, string[]>;

export function oracleConstrainsKeyMapping(
  constrainsMapping: OracleConstrains[],
): [OraclePkConstrain, OracleFkConstraint, OracleUkConstrain] {
  const pkConstraints = new Map<string, string[]>();
  const fkConstraints = new Map<
    string,
    {
      columns: string[];
      referencedOwner: string | null;
      referencedConstraintName: string | null;
      referencedTable: string | null;
    }
  >();
  const ukConstraints = new Map<string, string[]>();

  for (const constrain of constrainsMapping) {
    if (
      !constrain.CONSTRAINT_NAME ||
      !constrain.CONSTRAINT_TYPE ||
      !constrain.COLUMN_NAME
    )
      continue;

    switch (constrain.CONSTRAINT_TYPE) {
      case OracleConstrainType.P: {
        const columns = pkConstraints.get(constrain.CONSTRAINT_NAME) ?? [];
        columns.push(constrain.COLUMN_NAME);
        pkConstraints.set(constrain.CONSTRAINT_NAME, columns);
        break;
      }

      case OracleConstrainType.U: {
        const columns = ukConstraints.get(constrain.CONSTRAINT_NAME) ?? [];
        columns.push(constrain.COLUMN_NAME);
        ukConstraints.set(constrain.CONSTRAINT_NAME, columns);
        break;
      }

      case OracleConstrainType.R: {
        const current = fkConstraints.get(constrain.CONSTRAINT_NAME) ?? {
          columns: [],
          referencedOwner: constrain.R_OWNER ?? null,
          referencedConstraintName: constrain.R_CONSTRAINT_NAME ?? null,
          referencedTable: constrain.TABLE_NAME_2 ?? null,
        };

        current.columns.push(constrain.COLUMN_NAME);
        current.referencedOwner = constrain.R_OWNER ?? current.referencedOwner;
        current.referencedConstraintName =
          constrain.R_CONSTRAINT_NAME ?? current.referencedConstraintName;
        current.referencedTable =
          constrain.TABLE_NAME_2 ?? current.referencedTable;

        fkConstraints.set(constrain.CONSTRAINT_NAME, current);
        break;
      }

      default:
        break;
    }
  }
  return [pkConstraints, fkConstraints, ukConstraints];
}
