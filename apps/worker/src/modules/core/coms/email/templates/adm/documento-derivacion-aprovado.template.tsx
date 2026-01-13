import { DocumentoDerivacionAprobadoTemplateDto } from '@hsm-lib/definitions/dtos';

export function DocumentoDerivacionAprobadoTemplate(
  data: DocumentoDerivacionAprobadoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>Estimad@ paciente {data.patientName},</p>
      <p>
        {' '}
        Reciba un cordial saludo de parte del Centro Médico:{' '}
        <b>{data.medicalUnit}</b>.{' '}
      </p>
      <p>
        {' '}
        Le informamos que adjuntamos a este mensaje los documentos de aprobación
        para su derivación a la unidad asignada por el IESS.{' '}
      </p>
      <p>
        {' '}
        Para completar el proceso, deberá presentar este documento en formato
        digital al centro que se le ha asignado. No es necesario que se acerque
        a nuestras instalaciones.{' '}
      </p>
      <p>
        {' '}
        Agradecemos su confianza y quedamos a su disposición para cualquier
        consulta adicional, escriba al numero 0980892296 de nuestro asistente
        virtual SANTI{' '}
      </p>
      <p>Atentamente,</p>
      <p>
        Servicios al Paciente Centro Médico <b>{data.medicalUnit}</b>
      </p>
    </>
  );
}
