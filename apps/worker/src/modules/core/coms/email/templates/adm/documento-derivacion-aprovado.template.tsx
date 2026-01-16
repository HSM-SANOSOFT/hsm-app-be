import { DocumentoDerivacionAprobadoTemplateDto } from '@hsm-lib/definitions/dtos';

export function DocumentoDerivacionAprobadoTemplate(
  data: DocumentoDerivacionAprobadoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <i>{data.patientName}</i>
        <br />
        Reciba un cordial saludo de parte del Centro Médico: {data.medicalUnit}.
        <br />
        <br />
        Le informamos que adjuntamos a este mensaje los documentos de aprobación
        para su derivación a la unidad asignada por el IESS.
        <br />
        Para completar el proceso, deberá presentar este documento en formato
        digital al centro que se le ha asignado. No es necesario que se acerque
        a nuestras instalaciones.
        <br />
        <br />
        Atentamente,
        <br />
        Servicios al Paciente
        <br />
        Centro Médico {data.medicalUnit}
      </p>
    </>
  );
}
